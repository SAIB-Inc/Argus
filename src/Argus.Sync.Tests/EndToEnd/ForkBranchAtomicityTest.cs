using System.Globalization;
using Argus.Sync.Data.Stores;
using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Example.Reducers;
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
/// Per-branch atomicity at a fork (backlog P1-5). A fork's children commit in SEPARATE transactions,
/// so a fork is not atomic across its children: when one child faults the worker fails fast, but a
/// sibling (and the parent) that already committed is NOT rolled back. The crasher delays before
/// throwing so the survivor commits the same block first — proving the survivor's committed branch
/// survives the crasher's failure. Uses TestData/Blocks (no node required; Postgres only).
/// </summary>
public sealed class ForkBranchAtomicityTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
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
        if (_db is not null)
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
    public async Task ForkChildCrash_FailsFast_AndSiblingCommittedDataSurvives()
    {
        string testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            _output.WriteLine("SKIP: no TestData/Blocks/ to feed the worker.");
            return;
        }

        MockChainSyncProvider probe = new(testDataDir);
        IBlock[] blocks = [.. probe.AvailableBlocks.Take(2)];
        Assert.True(blocks.Length == 2, "need two test blocks (warmup + crash)");
        ulong warmSlot = blocks[0].Header().HeaderBody().Slot();
        ulong crashSlot = blocks[1].Header().HeaderBody().Slot();

        IDbContextFactory<TestDbContext> dbf = _db!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        IConfiguration config = BuildConfig(blocks[0]);
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        ILogger<CardanoIndexWorker> logger = loggerFactory.CreateLogger<CardanoIndexWorker>();
        IBlockUnitOfWorkFactory uowFactory = new EfBlockUnitOfWorkFactory<TestDbContext>(dbf);

        // Fork: BlockTestReducer (root) → { survivor, crasher }.
        List<IReducer> reducers = [new BlockTestReducer(), new ForkSurvivorReducer(), new ForkCrasherReducer(crashSlot)];
        MockChainProviderFactory factory = new(testDataDir);
        CardanoIndexWorker worker = new(config, logger, uowFactory, reducers, factory);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            MockChainSyncProvider provider = await WaitForProviderAsync(factory);
            await Task.Delay(500); // let the initial intersection rollback settle

            // Warm-up block processes cleanly through the whole fork.
            await provider.TriggerRollForwardAsync(warmSlot);
            await WaitForAsync(() => SurvivorCommittedAsync(dbf, warmSlot), TimeSpan.FromSeconds(10),
                "survivor did not commit the warm-up block");

            // Crash block: the survivor commits it (fast), while the crasher is still delaying.
            await provider.TriggerRollForwardAsync(crashSlot);
            await WaitForAsync(() => SurvivorCommittedAsync(dbf, crashSlot), TimeSpan.FromSeconds(10),
                "survivor did not commit the crash block before the crash");

            // The crasher's delayed throw now fails the worker fast.
            Task workerTask = worker.ExecuteTask ?? Task.CompletedTask;
            Task winner = await Task.WhenAny(workerTask, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.True(winner == workerTask, "worker did not fail fast after a fork child crashed");
            Assert.False(workerTask.IsCompletedSuccessfully, "the fork-child crash was swallowed — it must surface");

            // Per-branch atomicity: the sibling's AND the parent's commits for the crash block survive
            // the crasher's failure (separate transactions; the crash is not atomic across the fork).
            await using TestDbContext ctx = await dbf.CreateDbContextAsync();
            Assert.True(
                await ctx.WatchedAddressBalances.AnyAsync(s => s.Reducer == nameof(ForkSurvivorReducer) && s.Slot == crashSlot),
                "the sibling's crash-block commit must survive the other child's crash");
            Assert.True(
                await ctx.BlockTests.AnyAsync(b => b.Slot == crashSlot),
                "the parent's crash-block commit must survive");
            _output.WriteLine("Fork child crashed → worker failed fast; sibling + parent commits survived (per-branch atomicity).");
        }
        finally
        {
            try { await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8)); } catch { /* faulted task surfaces here */ }
            worker.Dispose();
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

    private static async Task<bool> SurvivorCommittedAsync(IDbContextFactory<TestDbContext> dbf, ulong slot)
    {
        await using TestDbContext ctx = await dbf.CreateDbContextAsync();
        return await ctx.WatchedAddressBalances.AsNoTracking()
            .AnyAsync(s => s.Reducer == nameof(ForkSurvivorReducer) && s.Slot == slot);
    }

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

    /// <summary>Fork dependent that commits a marker row per block (the surviving branch).</summary>
    [DependsOn(typeof(BlockTestReducer))]
    private sealed class ForkSurvivorReducer : IReducer
    {
        public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(block);
            ulong slot = block.Header().HeaderBody().Slot();
            TestDbContext ctx = uow.GetStorage<TestDbContext>();
            _ = ctx.WatchedAddressBalances.Add(new WatchedAddressBalanceSnapshot
            {
                Reducer = nameof(ForkSurvivorReducer),
                AddressName = "-",
                Address = "-",
                Slot = slot,
                Balance = 0,
            });
            return Task.CompletedTask;
        }

        public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
        {
            TestDbContext ctx = uow.GetStorage<TestDbContext>();
            ctx.WatchedAddressBalances.RemoveRange(
                ctx.WatchedAddressBalances.Where(s => s.Reducer == nameof(ForkSurvivorReducer) && s.Slot >= slot));
            return Task.CompletedTask;
        }
    }

    /// <summary>Fork sibling that delays (so the survivor commits first) then throws on a chosen slot.</summary>
    [DependsOn(typeof(BlockTestReducer))]
    private sealed class ForkCrasherReducer(ulong crashSlot) : IReducer
    {
        public async Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(block);
            if (block.Header().HeaderBody().Slot() == crashSlot)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Intentional fork-child crash at slot {crashSlot}");
            }
        }

        public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct) => Task.CompletedTask;
    }
}
