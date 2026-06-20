using System.Globalization;
using Argus.Sync.Data.Models;
using Argus.Sync.EntityFramework;
using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Tests.Infrastructure;
using Argus.Sync.Tests.Mocks;
using Argus.Sync.Workers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// End-to-end recovery test for the P0-3 crash/restart model.
///
/// Scenario: the worker commits block N (the "last good" block), then crashes while
/// processing block N+1 (a reducer fault). The N+1 transaction rolls back atomically, so
/// the persisted checkpoint stays at N. On restart a fresh worker re-intersects at N, and
/// Ouroboros opens with an EXCLUSIVE RollBackward to N — which keeps N's data and only
/// clears slots greater than N — then RollForward(N+1) replays the crashed block and sync
/// resumes with N+2. This proves the two cooperating guarantees (atomic commit + exclusive
/// reconnect rollback) compose into safe, no-data-loss recovery across a process restart.
///
/// Uses TestData/Blocks (no node required; Postgres only). The lowest three available
/// blocks play N (kept), N+1 (crashes then replays), and N+2 (resume) — the lowest block is
/// chosen deliberately because <see cref="MockChainSyncProvider"/> opens every connection
/// with an exclusive RollBackward to the lowest available block, faithfully simulating the
/// node rolling back to the persisted intersection on reconnect.
/// </summary>
public sealed class WorkerCrashRecoveryTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private readonly ITestOutputHelper _output = output;
    private TestDatabaseManager? _db;

    public Task InitializeAsync()
    {
        _db = new TestDatabaseManager(_output);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_db != null)
        {
            await _db.DisposeAsync();
            _db = null;
        }
    }

    public void Dispose()
    {
        _db?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _db = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CrashOnBlock_RestartRollsBackExclusiveKeepingLastGood_AndResumes()
    {
        string testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            _output.WriteLine("SKIP: no TestData/Blocks/ to feed the worker.");
            return;
        }

        MockChainSyncProvider probe = new(testDataDir);
        IBlock[] blocks = [.. probe.AvailableBlocks.Take(3)];
        Assert.True(blocks.Length == 3, "need at least three test blocks for N / N+1 / N+2");

        // blocks are slot-ascending; the lowest is N so the mock's reconnect rollback
        // (exclusive, to the lowest block) lands exactly on the block we must keep.
        ulong lastGoodSlot = blocks[0].Header().HeaderBody().Slot(); // N    — survives the crash
        ulong crashingSlot = blocks[1].Header().HeaderBody().Slot(); // N+1  — crashes, then replays
        ulong resumeSlot = blocks[2].Header().HeaderBody().Slot();   // N+2  — proves sync resumes
        _output.WriteLine($"N={lastGoodSlot} (keep), N+1={crashingSlot} (crash/replay), N+2={resumeSlot} (resume)");

        IDbContextFactory<TestDbContext> dbf = _db!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        IConfiguration config = BuildConfig(blocks[0]);
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ILogger<CardanoIndexWorker> logger = loggerFactory.CreateLogger<CardanoIndexWorker>();
        IBlockUnitOfWorkFactory uowFactory = new EfBlockUnitOfWorkFactory<TestDbContext>(dbf);

        // ---- Phase 1: crash while processing N+1 -------------------------------------
        MockChainProviderFactory crashFactory = new(testDataDir);
        List<IReducer> crashReducers = [new RecordingReducer(crashOnSlot: crashingSlot)];
        CardanoIndexWorker crashWorker = new(config, logger, uowFactory, crashReducers, crashFactory);

        try
        {
            await crashWorker.StartAsync(CancellationToken.None);
            MockChainSyncProvider crashProvider = await WaitForProviderAsync(crashFactory);
            await Task.Delay(500); // let the initial (no-op on empty DB) intersection rollback settle

            // Commit N, then confirm it persisted before triggering the crash.
            await crashProvider.TriggerRollForwardAsync(lastGoodSlot);
            await WaitForAsync(async () => await StateSlotAsync(dbf) == lastGoodSlot, TimeSpan.FromSeconds(10),
                "block N never committed");

            // Process N+1 — the reducer writes its row then throws, so the worker must
            // roll the whole N+1 transaction back and fail fast.
            await crashProvider.TriggerRollForwardAsync(crashingSlot);

            Task crashTask = crashWorker.ExecuteTask ?? Task.CompletedTask;
            Task winner = await Task.WhenAny(crashTask, Task.Delay(TimeSpan.FromSeconds(12)));
            Assert.True(winner == crashTask, "worker did not fail fast after the crash on N+1.");
            Assert.False(crashTask.IsCompletedSuccessfully, "crash was swallowed — the fault must surface.");

            // The last committed state is N; N+1 left nothing behind.
            Assert.Equal(lastGoodSlot, await StateSlotAsync(dbf));
            await using TestDbContext afterCrash = await dbf.CreateDbContextAsync();
            Assert.True(await afterCrash.BlockTests.AnyAsync(b => b.Slot == lastGoodSlot), "N must be committed");
            Assert.False(await afterCrash.BlockTests.AnyAsync(b => b.Slot == crashingSlot), "N+1 must have rolled back");
            _output.WriteLine("Phase 1: crashed on N+1; checkpoint at N, no partial N+1 data.");
        }
        finally
        {
            try { await crashWorker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8)); }
            catch { /* faulted ExecuteTask surfaces here; already asserted above */ }
            crashWorker.Dispose();
        }

        // ---- Phase 2: restart, exclusive rollback to N, replay N+1, resume to N+2 ----
        MockChainProviderFactory recoverFactory = new(testDataDir);
        List<IReducer> recoverReducers = [new RecordingReducer(crashOnSlot: null)];
        CardanoIndexWorker recoverWorker = new(config, logger, uowFactory, recoverReducers, recoverFactory);

        try
        {
            await recoverWorker.StartAsync(CancellationToken.None);
            MockChainSyncProvider recoverProvider = await WaitForProviderAsync(recoverFactory);

            // On reconnect the mock opens with an EXCLUSIVE RollBackward to N. Exclusive
            // keeps N and only clears slots > N, so N's data must survive untouched.
            await Task.Delay(500);
            Assert.Equal(lastGoodSlot, await StateSlotAsync(dbf));
            await using (TestDbContext afterReconnect = await dbf.CreateDbContextAsync())
            {
                Assert.True(await afterReconnect.BlockTests.AnyAsync(b => b.Slot == lastGoodSlot),
                    "exclusive rollback to N must NOT delete N's data");
            }

            // Replay N+1, then resume with N+2.
            await recoverProvider.TriggerRollForwardAsync(crashingSlot);
            await recoverProvider.TriggerRollForwardAsync(resumeSlot);
            await WaitForAsync(async () => await StateSlotAsync(dbf) == resumeSlot, TimeSpan.FromSeconds(10),
                "sync did not resume to N+2");

            await using TestDbContext final = await dbf.CreateDbContextAsync();
            // N kept exactly once (no duplicate from replay), N+1 replayed once, N+2 resumed.
            Assert.Equal(1, await final.BlockTests.CountAsync(b => b.Slot == lastGoodSlot));
            Assert.Equal(1, await final.BlockTests.CountAsync(b => b.Slot == crashingSlot));
            Assert.True(await final.BlockTests.AnyAsync(b => b.Slot == resumeSlot), "N+2 must be processed");
            _output.WriteLine("Phase 2: exclusive rollback kept N, replayed N+1, resumed to N+2.");
        }
        finally
        {
            foreach (MockChainSyncProvider p in recoverFactory.CreatedProviders)
            {
                p.CompleteChainSync();
            }
            try { await recoverWorker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8)); } catch { }
            recoverWorker.Dispose();
        }
    }

    private IConfiguration BuildConfig(IBlock firstBlock) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CardanoContext"] = _db!.DbContext.Database.GetConnectionString(),
            ["CardanoNodeConnection:Hash"] = firstBlock.Header().Hash(),
            ["CardanoNodeConnection:Slot"] = firstBlock.Header().HeaderBody().Slot().ToString(CultureInfo.InvariantCulture),
            ["Sync:Worker:ExitOnCompletion"] = "false",
            ["Sync:Dashboard:TuiMode"] = "false",
        }).Build();

    private static async Task<MockChainSyncProvider> WaitForProviderAsync(MockChainProviderFactory factory)
    {
        for (int i = 0; i < 100 && factory.CreatedProviders.Count < 1; i++)
        {
            await Task.Delay(100);
        }
        Assert.True(factory.CreatedProviders.Count >= 1, "worker never created a chain provider");
        return factory.CreatedProviders[0];
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout, string failMessage)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail(failMessage);
    }

    /// <summary>Highest checkpoint slot persisted for the recording reducer, or null if none.</summary>
    private static async Task<ulong?> StateSlotAsync(IDbContextFactory<TestDbContext> dbf)
    {
        await using TestDbContext db = await dbf.CreateDbContextAsync();
        ReducerState? state = await db.ReducerStates.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Name == nameof(RecordingReducer));
        if (state is null)
        {
            return null;
        }
        List<ulong> slots = [.. state.LatestIntersections.Select(p => p.Slot)];
        return slots.Count == 0 ? null : slots.Max();
    }

    /// <summary>
    /// Reducer that records each block as a <see cref="BlockTest"/> row and removes rows on
    /// rollback. Optionally crashes on a chosen slot (after writing its row) to simulate a
    /// process crash mid-block — the framework must then roll that block's transaction back.
    /// </summary>
    private sealed class RecordingReducer(ulong? crashOnSlot) : IReducer
    {
        public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(block);
            ulong slot = block.Header().HeaderBody().Slot();
            TestDbContext db = uow.GetStorage<TestDbContext>();
            _ = db.BlockTests.Add(new BlockTest(block.Header().Hash(), block.Header().HeaderBody().BlockNumber(), slot, DateTime.UtcNow));
            if (slot == crashOnSlot)
            {
                throw new InvalidOperationException($"Intentional crash at slot {slot}");
            }
            return Task.CompletedTask;
        }

        public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
        {
            TestDbContext db = uow.GetStorage<TestDbContext>();
            db.BlockTests.RemoveRange(db.BlockTests.Where(b => b.Slot >= slot));
            return Task.CompletedTask;
        }
    }
}
