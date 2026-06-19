using System.Globalization;
using Argus.Sync.Data.Stores;
using Argus.Sync.Example.Data;
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
/// Regression test for the P0 pipeline-fault hang (backlog item P0-1).
///
/// When a reducer throws, the worker must FAIL FAST, not deadlock. On the un-fixed
/// code the faulted <c>ReducerPipeline.RunAsync</c> task is never observed
/// (<c>CardanoIndexWorker._pipelineRunTasks</c> is populated at :248 but the worker
/// only awaits the chain-sync tasks at :277), and the dead reader's bounded-Wait inbox
/// backs up until the chain consumer's <c>EnqueueAsync</c> (:367) blocks forever — so
/// this test HANGS (fails on the timeout) until the CTS + task-observation fix lands.
///
/// A capacity-1 pipeline channel makes the deadlock trigger after just a couple of
/// blocks. Uses the existing TestData/Blocks (no node required; Postgres only).
/// </summary>
public sealed class PipelineFaultHangTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private readonly ITestOutputHelper _output = output;
    private TestDatabaseManager? _db;
    private MockChainProviderFactory? _mockFactory;

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
    public async Task ReducerThrows_WorkerFailsFast_DoesNotHang()
    {
        string testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            _output.WriteLine("SKIP: no TestData/Blocks/ to feed the worker.");
            return;
        }

        _mockFactory = new MockChainProviderFactory(testDataDir);
        MockChainSyncProvider probe = new(testDataDir);
        IBlock[] blocks = [.. probe.AvailableBlocks.Take(8)];
        Assert.True(blocks.Length >= 4, "need at least a few test blocks");

        ulong throwSlot = blocks[0].Header().HeaderBody().Slot();
        _output.WriteLine($"Reducer throws on slot {throwSlot}; feeding {blocks.Length} blocks at channel capacity 1.");

        IConfiguration config = BuildConfig(blocks[0]);
        IDbContextFactory<TestDbContext> dbf = _db!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ILogger<CardanoIndexWorker> logger = loggerFactory.CreateLogger<CardanoIndexWorker>();

        IBlockUnitOfWorkFactory uowFactory = new EfBlockUnitOfWorkFactory<TestDbContext>(dbf);
        List<IReducer> reducers = [new ThrowingReducer(throwSlot)];

        CardanoIndexWorker worker = new(config, logger, uowFactory, reducers, _mockFactory);

        try
        {
            await worker.StartAsync(CancellationToken.None);

            for (int i = 0; i < 100 && _mockFactory.CreatedProviders.Count < 1; i++)
            {
                await Task.Delay(100);
            }
            Assert.True(_mockFactory.CreatedProviders.Count >= 1, "worker never created a chain provider");
            await Task.Delay(1000); // let the initial intersection rollback settle

            // Feed blocks on a background task: the reducer throws on the first, and the
            // rest back up the dead reader's capacity-1 inbox until the chain consumer's
            // EnqueueAsync blocks. Fire-and-forget so the test thread isn't blocked if
            // the mock's own trigger channel fills behind the stuck worker.
            _ = Task.Run(async () =>
            {
                foreach (IBlock block in blocks)
                {
                    ulong slot = block.Header().HeaderBody().Slot();
                    foreach (MockChainSyncProvider provider in _mockFactory.CreatedProviders)
                    {
                        try { await provider.TriggerRollForwardAsync(slot); }
                        catch { return; }
                    }
                }
            });

            Task workerTask = worker.ExecuteTask ?? Task.CompletedTask;
            Task winner = await Task.WhenAny(workerTask, Task.Delay(TimeSpan.FromSeconds(12)));

            Assert.True(
                winner == workerTask,
                "P0-1 HANG: a reducer threw but the worker did not terminate within 12s. The faulted pipeline " +
                "task is unobserved and bounded-channel backpressure deadlocks the chain consumer. Fix: cancel a " +
                "worker-owned CancellationTokenSource on any pipeline fault and observe _pipelineRunTasks.");

            // It terminated — make sure it did so by surfacing the fault, not by silently
            // succeeding (which would mean the reducer error was swallowed).
            Assert.False(
                workerTask.IsCompletedSuccessfully,
                "worker terminated by completing successfully — the reducer fault was swallowed; it must surface.");

            _output.WriteLine("Worker failed fast (did not deadlock) and surfaced the reducer fault.");
        }
        finally
        {
            try { await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8)); }
            catch { /* StopAsync cancels stoppingToken to unblock the stuck EnqueueAsync */ }
            worker.Dispose();
            loggerFactory.Dispose();
        }
    }

    private IConfiguration BuildConfig(IBlock firstBlock) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CardanoContext"] = _db!.DbContext.Database.GetConnectionString(),
            ["CardanoNodeConnection:Hash"] = firstBlock.Header().Hash(),
            ["CardanoNodeConnection:Slot"] = firstBlock.Header().HeaderBody().Slot().ToString(CultureInfo.InvariantCulture),
            ["Sync:Pipeline:ChannelCapacity"] = "1",
            ["Sync:Worker:ExitOnCompletion"] = "false",
            ["Sync:Dashboard:TuiMode"] = "false",
        }).Build();

    /// <summary>Test reducer that throws on a chosen slot to simulate a reducer fault.</summary>
    private sealed class ThrowingReducer(ulong throwOnSlot) : IReducer
    {
        public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(block);
            if (block.Header().HeaderBody().Slot() == throwOnSlot)
            {
                throw new InvalidOperationException($"Intentional reducer fault at slot {throwOnSlot}");
            }

            return Task.CompletedTask;
        }

        public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct) => Task.CompletedTask;
    }
}
