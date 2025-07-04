using Argus.Sync.Data.Models;
using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Tests.Infrastructure;
using Argus.Sync.Tests.Mocks;
using Argus.Sync.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Concurrent;
using System.Reflection;

namespace Argus.Sync.Tests.EndToEnd;

[Collection("Database collection")]
public class StartPointLogicTest : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TestDatabaseManager? _databaseManager;
    
    public StartPointLogicTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
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
    
    [Fact]
    public async Task StartPointLogic_ShouldAdjustDependentToMatchDependency()
    {
        // Setup
        var dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using var dbContext = await dbContextFactory.CreateDbContextAsync();
        
        // Pre-populate reducer states: BlockTestReducer has processed up to slot 1000
        var blockReducerState = new ReducerState("BlockTestReducer", DateTimeOffset.UtcNow)
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
        var dependentReducerState = new ReducerState("DependentTransactionReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = []
        };
        
        dbContext.ReducerStates.Add(blockReducerState);
        dbContext.ReducerStates.Add(dependentReducerState);
        await dbContext.SaveChangesAsync();
        
        // Create reducers
        var blockReducer = new BlockTestReducer(dbContextFactory);
        var dependentReducer = new DependentTransactionReducer(dbContextFactory);
        var reducers = new List<IReducer<IReducerModel>> { blockReducer, dependentReducer };
        
        // Use reflection to test the initialization logic directly
        var workerType = typeof(CardanoIndexWorker<TestDbContext>);
        var initMethod = workerType.GetMethod("InitializeAllReducerStatesAsync", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CardanoNodeConnection:NetworkMagic"] = "2",
                ["CardanoNodeConnection:Slot"] = "0",
                ["CardanoNodeConnection:Hash"] = "genesis"
            })
            .Build();
            
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<CardanoIndexWorker<TestDbContext>>();
        var mockProviderFactory = new MockChainProviderFactory(Path.Combine(Directory.GetCurrentDirectory(), "TestData"));
        
        var worker = new CardanoIndexWorker<TestDbContext>(
            configuration,
            logger,
            dbContextFactory,
            reducers,
            mockProviderFactory
        );
        
        // Build dependency graph first
        var buildGraphMethod = workerType.GetMethod("BuildDependencyGraph", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        buildGraphMethod!.Invoke(worker, null);
        
        // Act - Call InitializeAllReducerStatesAsync
        var cts = new CancellationTokenSource();
        await (Task)initMethod!.Invoke(worker, [cts.Token])!;
        
        // Get the internal reducer states
        var reducerStatesField = workerType.GetField("_reducerStates", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var reducerStates = reducerStatesField!.GetValue(worker) as ConcurrentDictionary<string, ReducerState>;
        
        var adjustedDependentState = reducerStates!["DependentTransactionReducer"];
        
        _output.WriteLine($"Original dependent start: slot 0");
        _output.WriteLine($"Adjusted dependent start: slot {adjustedDependentState.StartIntersection.Slot}");
        _output.WriteLine($"Adjusted dependent hash: {adjustedDependentState.StartIntersection.Hash}");
        
        // Assert - Dependent should be adjusted to match dependency's latest point
        Assert.Equal(1000UL, adjustedDependentState.StartIntersection.Slot);
        Assert.Equal("hash1000", adjustedDependentState.StartIntersection.Hash);
    }
    
    [Fact]
    public async Task StartPointLogic_ShouldHandleChainedDependencies()
    {
        // Setup
        var dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using var dbContext = await dbContextFactory.CreateDbContextAsync();
        
        // Pre-populate states: A at 1000, B at 500, C at 0
        var blockReducerState = new ReducerState("BlockTestReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = [new Point("hash1000", 1000)]
        };
        
        var dependentReducerState = new ReducerState("DependentTransactionReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = [new Point("hash500", 500)]
        };
        
        var chainedReducerState = new ReducerState("ChainedDependentReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = Enumerable.Empty<Point>()
        };
        
        dbContext.ReducerStates.AddRange(blockReducerState, dependentReducerState, chainedReducerState);
        await dbContext.SaveChangesAsync();
        
        // Create all reducers
        var reducers = new List<IReducer<IReducerModel>>
        {
            new BlockTestReducer(dbContextFactory),
            new DependentTransactionReducer(dbContextFactory),
            new ChainedDependentReducer(dbContextFactory)
        };
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CardanoNodeConnection:NetworkMagic"] = "2",
                ["CardanoNodeConnection:Slot"] = "0",
                ["CardanoNodeConnection:Hash"] = "genesis"
            })
            .Build();
            
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<CardanoIndexWorker<TestDbContext>>();
        var mockProviderFactory = new MockChainProviderFactory(Path.Combine(Directory.GetCurrentDirectory(), "TestData"));
        
        var worker = new CardanoIndexWorker<TestDbContext>(
            configuration,
            logger,
            dbContextFactory,
            reducers,
            mockProviderFactory
        );
        
        // Use reflection to test internal methods
        var workerType = typeof(CardanoIndexWorker<TestDbContext>);
        var buildGraphMethod = workerType.GetMethod("BuildDependencyGraph", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        buildGraphMethod!.Invoke(worker, null);
        
        var initMethod = workerType.GetMethod("InitializeAllReducerStatesAsync", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var cts = new CancellationTokenSource();
        await (Task)initMethod!.Invoke(worker, [cts.Token])!;
        
        // Get reducer states
        var reducerStatesField = workerType.GetField("_reducerStates", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var reducerStates = reducerStatesField!.GetValue(worker) as ConcurrentDictionary<string, ReducerState>;
        
        var blockState = reducerStates!["BlockTestReducer"];
        var dependentState = reducerStates!["DependentTransactionReducer"];
        var chainedState = reducerStates!["ChainedDependentReducer"];
        
        _output.WriteLine($"BlockTestReducer: slot {blockState.StartIntersection.Slot}");
        _output.WriteLine($"DependentTransactionReducer: slot {dependentState.StartIntersection.Slot}");
        _output.WriteLine($"ChainedDependentReducer: slot {chainedState.StartIntersection.Slot}");
        
        // Assert - Chain should be properly adjusted
        Assert.Equal(0UL, blockState.StartIntersection.Slot); // Root stays at 0
        Assert.Equal(1000UL, dependentState.StartIntersection.Slot); // Adjusted to match BlockTest
        Assert.Equal(500UL, chainedState.StartIntersection.Slot); // Adjusted to match Dependent
    }
    
    [Fact]
    public async Task StartPointLogic_ShouldHandleBootstrapCase()
    {
        // Setup - All reducers at initial state
        var dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using var dbContext = await dbContextFactory.CreateDbContextAsync();
        
        var blockReducerState = new ReducerState("BlockTestReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = Enumerable.Empty<Point>()
        };
        
        var dependentReducerState = new ReducerState("DependentTransactionReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = Enumerable.Empty<Point>()
        };
        
        dbContext.ReducerStates.AddRange(blockReducerState, dependentReducerState);
        await dbContext.SaveChangesAsync();
        
        // Create reducers
        var reducers = new List<IReducer<IReducerModel>>
        {
            new BlockTestReducer(dbContextFactory),
            new DependentTransactionReducer(dbContextFactory)
        };
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CardanoNodeConnection:NetworkMagic"] = "2",
                ["CardanoNodeConnection:Slot"] = "0",
                ["CardanoNodeConnection:Hash"] = "genesis"
            })
            .Build();
            
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<CardanoIndexWorker<TestDbContext>>();
        var mockProviderFactory = new MockChainProviderFactory(Path.Combine(Directory.GetCurrentDirectory(), "TestData"));
        
        var worker = new CardanoIndexWorker<TestDbContext>(
            configuration,
            logger,
            dbContextFactory,
            reducers,
            mockProviderFactory
        );
        
        // Use reflection
        var workerType = typeof(CardanoIndexWorker<TestDbContext>);
        var buildGraphMethod = workerType.GetMethod("BuildDependencyGraph", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        buildGraphMethod!.Invoke(worker, null);
        
        var initMethod = workerType.GetMethod("InitializeAllReducerStatesAsync", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var cts = new CancellationTokenSource();
        await (Task)initMethod!.Invoke(worker, [cts.Token])!;
        
        // Get reducer states
        var reducerStatesField = workerType.GetField("_reducerStates", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var reducerStates = reducerStatesField!.GetValue(worker) as ConcurrentDictionary<string, ReducerState>;
        
        var blockState = reducerStates!["BlockTestReducer"];
        var dependentState = reducerStates!["DependentTransactionReducer"];
        
        _output.WriteLine($"BlockTestReducer: slot {blockState.StartIntersection.Slot}");
        _output.WriteLine($"DependentTransactionReducer: slot {dependentState.StartIntersection.Slot}");
        
        // Assert - Both should remain at initial state
        Assert.Equal(0UL, blockState.StartIntersection.Slot);
        Assert.Equal(0UL, dependentState.StartIntersection.Slot);
        Assert.Equal("genesis", blockState.StartIntersection.Hash);
        Assert.Equal("genesis", dependentState.StartIntersection.Hash);
    }
    
    [Fact]
    public Task ShouldProcessBlock_ShouldRespectDependencyState()
    {
        // Setup
        var dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        var reducers = new List<IReducer<IReducerModel>>
        {
            new BlockTestReducer(dbContextFactory),
            new DependentTransactionReducer(dbContextFactory)
        };
        
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CardanoNodeConnection:NetworkMagic"] = "2",
                ["CardanoNodeConnection:Slot"] = "0",
                ["CardanoNodeConnection:Hash"] = "genesis"
            })
            .Build();
            
        var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<CardanoIndexWorker<TestDbContext>>();
        var mockProviderFactory = new MockChainProviderFactory(Path.Combine(Directory.GetCurrentDirectory(), "TestData"));
        
        var worker = new CardanoIndexWorker<TestDbContext>(
            configuration,
            logger,
            dbContextFactory,
            reducers,
            mockProviderFactory
        );
        
        // Setup internal state using reflection
        var workerType = typeof(CardanoIndexWorker<TestDbContext>);
        
        // Build dependency graph
        var buildGraphMethod = workerType.GetMethod("BuildDependencyGraph", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        buildGraphMethod!.Invoke(worker, null);
        
        // Set up reducer states
        var reducerStatesField = workerType.GetField("_reducerStates", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var reducerStates = reducerStatesField!.GetValue(worker) as ConcurrentDictionary<string, ReducerState>;
        
        // BlockTestReducer at slot 1000
        reducerStates!["BlockTestReducer"] = new ReducerState("BlockTestReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections = [new Point("hash1000", 1000)]
        };
        
        // DependentTransactionReducer at slot 500
        reducerStates!["DependentTransactionReducer"] = new ReducerState("DependentTransactionReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("hash500", 500),
            LatestIntersections = [new Point("hash500", 500)]
        };
        
        // Test ShouldProcessBlock
        var shouldProcessMethod = workerType.GetMethod("ShouldProcessBlock", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        // Test 1: Root reducer should process any block >= start
        var rootShouldProcess = (bool)shouldProcessMethod!.Invoke(worker, ["BlockTestReducer", 1500UL])!;
        Assert.True(rootShouldProcess);
        
        // Test 2: Dependent should not process block beyond dependency
        var dependentShouldNotProcess = (bool)shouldProcessMethod!.Invoke(worker, ["DependentTransactionReducer", 1500UL])!;
        Assert.False(dependentShouldNotProcess);
        
        // Test 3: Dependent should process block within dependency range
        var dependentShouldProcess = (bool)shouldProcessMethod!.Invoke(worker, ["DependentTransactionReducer", 800UL])!;
        Assert.True(dependentShouldProcess);
        
        // Test 4: Should not process block before start point
        var shouldNotProcessBeforeStart = (bool)shouldProcessMethod!.Invoke(worker, ["DependentTransactionReducer", 400UL])!;
        Assert.False(shouldNotProcessBeforeStart);
        
        _output.WriteLine("ShouldProcessBlock tests passed");
        
        return Task.CompletedTask;
    }
}