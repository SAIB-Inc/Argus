using System.Globalization;
using Argus.Sync.Data.Stores;
using Argus.Sync.Example.Data;
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
/// Regression test for P0-2 (shutdown semantics). With multiple independent root reducers,
/// the worker must wait for ALL roots to finish before exiting (WhenAll), not exit the
/// moment the FIRST one ends (WhenAny). On the pre-fix code, completing one root's chain
/// sync tore down the whole worker while the other root was still healthy.
/// </summary>
public sealed class WorkerShutdownSemanticsTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
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
    public async Task OneRootFinishing_DoesNotExitWorker_UntilAllRootsFinish()
    {
        string testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            _output.WriteLine("SKIP: no TestData/Blocks/.");
            return;
        }

        _mockFactory = new MockChainProviderFactory(testDataDir);
        MockChainSyncProvider probe = new(testDataDir);
        IBlock firstBlock = probe.AvailableBlocks[0];

        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CardanoContext"] = _db!.DbContext.Database.GetConnectionString(),
            ["CardanoNodeConnection:Hash"] = firstBlock.Header().Hash(),
            ["CardanoNodeConnection:Slot"] = firstBlock.Header().HeaderBody().Slot().ToString(CultureInfo.InvariantCulture),
            ["Sync:Worker:ExitOnCompletion"] = "false",
            ["Sync:Dashboard:TuiMode"] = "false",
        }).Build();

        IDbContextFactory<TestDbContext> dbf = _db.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ILogger<CardanoIndexWorker> logger = loggerFactory.CreateLogger<CardanoIndexWorker>();
        IBlockUnitOfWorkFactory uowFactory = new EfBlockUnitOfWorkFactory<TestDbContext>(dbf);

        // Two independent ROOT reducers (distinct types) -> two chain providers.
        List<IReducer> reducers = [new BlockTestReducer(), new TransactionTestReducer()];
        CardanoIndexWorker worker = new(config, logger, uowFactory, reducers, _mockFactory);

        try
        {
            await worker.StartAsync(CancellationToken.None);

            for (int i = 0; i < 100 && _mockFactory.CreatedProviders.Count < 2; i++)
            {
                await Task.Delay(100);
            }
            Assert.Equal(2, _mockFactory.CreatedProviders.Count);
            await Task.Delay(1000); // let the initial rollbacks settle

            Task workerTask = worker.ExecuteTask ?? Task.CompletedTask;

            // Finish ONE root's chain sync. The worker must keep running because the other
            // root is still healthy. On the pre-fix WhenAny code, this exited the worker.
            _mockFactory.CreatedProviders[0].CompleteChainSync();
            await Task.Delay(2500);
            Assert.False(
                workerTask.IsCompleted,
                "P0-2: worker exited after only ONE of two roots finished (WhenAny). It must wait for all roots (WhenAll).");

            // Finish the second root — now the worker should complete cleanly.
            _mockFactory.CreatedProviders[1].CompleteChainSync();
            Task winner = await Task.WhenAny(workerTask, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(winner == workerTask, "worker did not exit after all roots finished.");
            Assert.True(workerTask.IsCompletedSuccessfully, "worker should exit cleanly when all roots finish normally.");
            _output.WriteLine("Worker waited for all roots, then exited cleanly.");
        }
        finally
        {
            try { await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8)); }
            catch { /* unblock + ignore shutdown errors */ }
            worker.Dispose();
            loggerFactory.Dispose();
        }
    }
}
