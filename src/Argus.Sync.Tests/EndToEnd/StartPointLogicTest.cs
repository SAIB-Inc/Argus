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

    private static (CardanoIndexWorker<TestDbContext> Worker, ILoggerFactory LoggerFactory, CancellationTokenSource Cts) CreateWorkerWithReducers(
        IDbContextFactory<TestDbContext> dbContextFactory,
        List<IReducer<IReducerModel>> reducers)
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
        ILogger<CardanoIndexWorker<TestDbContext>> logger = loggerFactory.CreateLogger<CardanoIndexWorker<TestDbContext>>();
        MockChainProviderFactory mockProviderFactory = new(Path.Combine(Directory.GetCurrentDirectory(), "TestData"));

        CardanoIndexWorker<TestDbContext> worker = new(
            configuration,
            logger,
            dbContextFactory,
            reducers,
            mockProviderFactory
        );

        return (worker, loggerFactory, new CancellationTokenSource());
    }

    private static async Task<ConcurrentDictionary<string, ReducerState>> BuildGraphAndInitializeAsync(
        CardanoIndexWorker<TestDbContext> worker,
        CancellationToken cancellationToken)
    {
        Type workerType = typeof(CardanoIndexWorker<TestDbContext>);

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
        BlockTestReducer blockReducer = new(dbContextFactory);
        DependentTransactionReducer dependentReducer = new(dbContextFactory);
        List<IReducer<IReducerModel>> reducers = [blockReducer, dependentReducer];

        (CardanoIndexWorker<TestDbContext> worker, ILoggerFactory loggerFactory, CancellationTokenSource cts) = CreateWorkerWithReducers(dbContextFactory, reducers);
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
        List<IReducer<IReducerModel>> reducers =
        [
            new BlockTestReducer(dbContextFactory),
            new DependentTransactionReducer(dbContextFactory),
            new ChainedDependentReducer(dbContextFactory)
        ];

        (CardanoIndexWorker<TestDbContext> worker, ILoggerFactory loggerFactory, CancellationTokenSource cts) = CreateWorkerWithReducers(dbContextFactory, reducers);
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
        List<IReducer<IReducerModel>> reducers =
        [
            new BlockTestReducer(dbContextFactory),
            new DependentTransactionReducer(dbContextFactory)
        ];

        (CardanoIndexWorker<TestDbContext> worker, ILoggerFactory loggerFactory, CancellationTokenSource cts) = CreateWorkerWithReducers(dbContextFactory, reducers);
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

    [Fact]
    [Trait("Category", "Integration")]
    public Task ShouldProcessBlock_ShouldRespectDependencyState()
    {
        // Setup
        IDbContextFactory<TestDbContext> dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        List<IReducer<IReducerModel>> reducers =
        [
            new BlockTestReducer(dbContextFactory),
            new DependentTransactionReducer(dbContextFactory)
        ];

        (CardanoIndexWorker<TestDbContext> worker, ILoggerFactory loggerFactory, CancellationTokenSource cts) = CreateWorkerWithReducers(dbContextFactory, reducers);
        using (loggerFactory)
        using (cts)
        using (worker)
        {
            // Setup internal state using reflection
            Type workerType = typeof(CardanoIndexWorker<TestDbContext>);

            // Build dependency graph
            MethodInfo? buildGraphMethod = workerType.GetMethod("BuildDependencyGraph",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _ = buildGraphMethod!.Invoke(worker, null);

            // Set up reducer states
            FieldInfo? reducerStatesField = workerType.GetField("_reducerStates",
                BindingFlags.NonPublic | BindingFlags.Instance);
            ConcurrentDictionary<string, ReducerState>? reducerStates = reducerStatesField!.GetValue(worker) as ConcurrentDictionary<string, ReducerState>;

            // BlockTestReducer at slot 1000
            reducerStates!["BlockTestReducer"] = new ReducerState("BlockTestReducer", DateTimeOffset.UtcNow)
            {
                StartIntersection = new Point("genesis", 0),
                LatestIntersections = [new Point("hash1000", 1000)]
            };

            // DependentTransactionReducer at slot 500
            reducerStates["DependentTransactionReducer"] = new ReducerState("DependentTransactionReducer", DateTimeOffset.UtcNow)
            {
                StartIntersection = new Point("hash500", 500),
                LatestIntersections = [new Point("hash500", 500)]
            };

            // Also populate _latestSlots so ShouldProcessBlock can check dependency progress
            FieldInfo? latestSlotsField = workerType.GetField("_latestSlots",
                BindingFlags.NonPublic | BindingFlags.Instance);
            ConcurrentDictionary<string, ulong>? latestSlots = latestSlotsField!.GetValue(worker) as ConcurrentDictionary<string, ulong>;
            latestSlots!["BlockTestReducer"] = 1000;
            latestSlots["DependentTransactionReducer"] = 500;

            // Test ShouldProcessBlock
            MethodInfo? shouldProcessMethod = workerType.GetMethod("ShouldProcessBlock",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // Test 1: Root reducer should process any block >= start
            bool rootShouldProcess = (bool)shouldProcessMethod!.Invoke(worker, ["BlockTestReducer", 1500UL])!;
            Assert.True(rootShouldProcess);

            // Test 2: Dependent should not process block beyond dependency
            bool dependentShouldNotProcess = (bool)shouldProcessMethod!.Invoke(worker, ["DependentTransactionReducer", 1500UL])!;
            Assert.False(dependentShouldNotProcess);

            // Test 3: Dependent should process block within dependency range
            bool dependentShouldProcess = (bool)shouldProcessMethod!.Invoke(worker, ["DependentTransactionReducer", 800UL])!;
            Assert.True(dependentShouldProcess);

            // Test 4: Should not process block before start point
            bool shouldNotProcessBeforeStart = (bool)shouldProcessMethod!.Invoke(worker, ["DependentTransactionReducer", 400UL])!;
            Assert.False(shouldNotProcessBeforeStart);

            _output.WriteLine("ShouldProcessBlock tests passed");
        }

        return Task.CompletedTask;
    }
}
