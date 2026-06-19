using Argus.Sync.Data.Models;
using Argus.Sync.Example.Data;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Reducers;
using Argus.Sync.Tests.Infrastructure;
using Argus.Sync.Tests.Mocks;
using Argus.Sync.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using System.Collections.Concurrent;
using System.Reflection;

namespace Argus.Sync.Tests.EndToEnd;

[Collection("Database collection")]
public class StartPointLogicTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private readonly ITestOutputHelper _output = output;
    private TestDatabaseManager? _databaseManager;

    public async Task InitializeAsync()
    {
        _databaseManager = new TestDatabaseManager(_output);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_databaseManager != null)
        {
            await _databaseManager.DisposeAsync();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && _databaseManager != null)
        {
            _databaseManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _databaseManager = null;
        }
    }

    private static (CardanoIndexWorker Worker, ILoggerFactory LoggerFactory, CancellationTokenSource Cts) CreateWorkerWithReducers(
        IDbContextFactory<TestDbContext> dbContextFactory,
        List<IReducer> reducers)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CardanoNodeConnection:NetworkMagic"] = "2",
                ["CardanoNodeConnection:Slot"] = "0",
                ["CardanoNodeConnection:Hash"] = "genesis"
            })
            .Build();

        ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole());
        ILogger<CardanoIndexWorker> logger = loggerFactory.CreateLogger<CardanoIndexWorker>();
        MockChainProviderFactory mockProviderFactory = new(Path.Combine(Directory.GetCurrentDirectory(), "TestData"));

        Argus.Sync.Reducers.IBlockUnitOfWorkFactory uowFactory = new Argus.Sync.Data.Stores.EfBlockUnitOfWorkFactory<TestDbContext>(dbContextFactory);
        CardanoIndexWorker worker = new(
            configuration,
            logger,
            uowFactory,
            reducers,
            mockProviderFactory
        );

        return (worker, loggerFactory, new CancellationTokenSource());
    }

    private static async Task<ConcurrentDictionary<string, ReducerState>> BuildGraphAndInitializeAsync(
        CardanoIndexWorker worker,
        CancellationToken cancellationToken)
    {
        Type workerType = typeof(CardanoIndexWorker);

        MethodInfo? buildGraphMethod = workerType.GetMethod("BuildDependencyGraph",
            BindingFlags.NonPublic | BindingFlags.Instance);
        _ = buildGraphMethod!.Invoke(worker, null);

        MethodInfo? initMethod = workerType.GetMethod("InitializeAllReducerStatesAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        await (Task)initMethod!.Invoke(worker, [cancellationToken])!;

        FieldInfo? reducerStatesField = workerType.GetField("_reducerStates",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return (reducerStatesField!.GetValue(worker) as ConcurrentDictionary<string, ReducerState>)!;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartPointLogic_ShouldAdjustDependentToMatchDependency()
    {
        // Setup
        IDbContextFactory<TestDbContext> dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        // Pre-populate reducer states: BlockTestReducer has processed up to slot 1000
        ReducerState blockReducerState = new("BlockTestReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections =
            [
                new Point("hash500", 500),
                new Point("hash750", 750),
                new Point("hash1000", 1000)
            ]
        };

        // DependentTransactionReducer hasn't started yet
        ReducerState dependentReducerState = new("DependentTransactionReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = []
        };

        _ = dbContext.ReducerStates.Add(blockReducerState);
        _ = dbContext.ReducerStates.Add(dependentReducerState);
        _ = await dbContext.SaveChangesAsync();

        // Create reducers
        BlockTestReducer blockReducer = new();
        DependentTransactionReducer dependentReducer = new();
        List<IReducer> reducers = [blockReducer, dependentReducer];

        (CardanoIndexWorker worker, ILoggerFactory loggerFactory, CancellationTokenSource cts) = CreateWorkerWithReducers(dbContextFactory, reducers);
        using (loggerFactory)
        using (cts)
        using (worker)
        {
            ConcurrentDictionary<string, ReducerState> reducerStates = await BuildGraphAndInitializeAsync(worker, cts.Token);

            ReducerState adjustedDependentState = reducerStates["DependentTransactionReducer"];

            _output.WriteLine($"Original dependent start: slot 0");
            _output.WriteLine($"Adjusted dependent start: slot {adjustedDependentState.StartIntersection.Slot}");
            _output.WriteLine($"Adjusted dependent hash: {adjustedDependentState.StartIntersection.Hash}");

            // Assert - Dependent should be adjusted to match dependency's latest point
            Assert.Equal(1000UL, adjustedDependentState.StartIntersection.Slot);
            Assert.Equal("hash1000", adjustedDependentState.StartIntersection.Hash);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartPointLogic_ShouldHandleChainedDependencies()
    {
        // Setup
        IDbContextFactory<TestDbContext> dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        // Pre-populate states: A at 1000, B at 500, C at 0
        ReducerState blockReducerState = new("BlockTestReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = [new Point("hash1000", 1000)]
        };

        ReducerState dependentReducerState = new("DependentTransactionReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = [new Point("hash500", 500)]
        };

        ReducerState chainedReducerState = new("ChainedDependentReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = []
        };

        dbContext.ReducerStates.AddRange(blockReducerState, dependentReducerState, chainedReducerState);
        _ = await dbContext.SaveChangesAsync();

        // Create all reducers
        List<IReducer> reducers =
        [
            new BlockTestReducer(),
            new DependentTransactionReducer(),
            new ChainedDependentReducer()
        ];

        (CardanoIndexWorker worker, ILoggerFactory loggerFactory, CancellationTokenSource cts) = CreateWorkerWithReducers(dbContextFactory, reducers);
        using (loggerFactory)
        using (cts)
        using (worker)
        {
            ConcurrentDictionary<string, ReducerState> reducerStates = await BuildGraphAndInitializeAsync(worker, cts.Token);

            ReducerState blockState = reducerStates["BlockTestReducer"];
            ReducerState dependentState = reducerStates["DependentTransactionReducer"];
            ReducerState chainedState = reducerStates["ChainedDependentReducer"];

            _output.WriteLine($"BlockTestReducer: slot {blockState.StartIntersection.Slot}");
            _output.WriteLine($"DependentTransactionReducer: slot {dependentState.StartIntersection.Slot}");
            _output.WriteLine($"ChainedDependentReducer: slot {chainedState.StartIntersection.Slot}");

            // Assert - Chain should be properly adjusted
            Assert.Equal(0UL, blockState.StartIntersection.Slot); // Root stays at 0
            Assert.Equal(1000UL, dependentState.StartIntersection.Slot); // Adjusted to match BlockTest
            Assert.Equal(500UL, chainedState.StartIntersection.Slot); // Adjusted to match Dependent
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartPointLogic_ShouldHandleBootstrapCase()
    {
        // Setup - All reducers at initial state
        IDbContextFactory<TestDbContext> dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        ReducerState blockReducerState = new("BlockTestReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = []
        };

        ReducerState dependentReducerState = new("DependentTransactionReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = []
        };

        dbContext.ReducerStates.AddRange(blockReducerState, dependentReducerState);
        _ = await dbContext.SaveChangesAsync();

        // Create reducers
        List<IReducer> reducers =
        [
            new BlockTestReducer(),
            new DependentTransactionReducer()
        ];

        (CardanoIndexWorker worker, ILoggerFactory loggerFactory, CancellationTokenSource cts) = CreateWorkerWithReducers(dbContextFactory, reducers);
        using (loggerFactory)
        using (cts)
        using (worker)
        {
            ConcurrentDictionary<string, ReducerState> reducerStates = await BuildGraphAndInitializeAsync(worker, cts.Token);

            ReducerState blockState = reducerStates["BlockTestReducer"];
            ReducerState dependentState = reducerStates["DependentTransactionReducer"];

            _output.WriteLine($"BlockTestReducer: slot {blockState.StartIntersection.Slot}");
            _output.WriteLine($"DependentTransactionReducer: slot {dependentState.StartIntersection.Slot}");

            // Assert - Both should remain at initial state
            Assert.Equal(0UL, blockState.StartIntersection.Slot);
            Assert.Equal(0UL, dependentState.StartIntersection.Slot);
            Assert.Equal("genesis", blockState.StartIntersection.Hash);
            Assert.Equal("genesis", dependentState.StartIntersection.Hash);
        }
    }

    // Note: ShouldProcessBlock_ShouldRespectDependencyState was removed in
    // Commit 3. The `ShouldProcessBlock` runtime check it tested has been
    // deleted from CardanoIndexWorker — the channel pipeline structurally
    // enforces parent-before-dependent ordering (dependents only receive
    // envelopes their parent has already pushed downstream), making the
    // dynamic runtime guard redundant. The remaining StartPointLogic tests
    // in this class still cover the warm-start adjustment path.
}
