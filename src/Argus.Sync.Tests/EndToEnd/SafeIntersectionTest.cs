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
using System.Reflection;

namespace Argus.Sync.Tests.EndToEnd;

[Collection("Database collection")]
public class SafeIntersectionTest : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TestDatabaseManager? _databaseManager;
    
    public SafeIntersectionTest(ITestOutputHelper output)
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
    public async Task SafeIntersection_ShouldUseOldestPointForRootReducerWithDependents()
    {
        // Setup
        var dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using var dbContext = await dbContextFactory.CreateDbContextAsync();
        
        // Pre-populate reducer states with misaligned intersection points
        // This simulates what happens when reducers are stopped at different times
        var blockReducerState = new ReducerState("BlockTestReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections =
            [
                new Point("hash800", 800),
                new Point("hash900", 900),
                new Point("hash1000", 1000)  // BlockTestReducer is at slot 1000
            ]
        };
        
        // DependentTransactionReducer is slightly behind at slot 995
        var dependentReducerState = new ReducerState("DependentTransactionReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections =
            [
                new Point("hash795", 795),
                new Point("hash895", 895),
                new Point("hash995", 995)  // 5 slots behind
            ]
        };
        
        // ChainedDependentReducer is even further behind at slot 990
        var chainedReducerState = new ReducerState("ChainedDependentReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections =
            [
                new Point("hash790", 790),
                new Point("hash890", 890),
                new Point("hash990", 990)  // 10 slots behind
            ]
        };
        
        dbContext.ReducerStates.AddRange(blockReducerState, dependentReducerState, chainedReducerState);
        await dbContext.SaveChangesAsync();
        
        // Create reducers
        var blockReducer = new BlockTestReducer(dbContextFactory);
        var dependentReducer = new DependentTransactionReducer(dbContextFactory);
        var chainedReducer = new ChainedDependentReducer(dbContextFactory);
        var reducers = new List<IReducer<IReducerModel>> { blockReducer, dependentReducer, chainedReducer };
        
        // Create configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CardanoNodeConnection:NetworkMagic"] = "2",
                ["CardanoNodeConnection:Slot"] = "0",
                ["CardanoNodeConnection:Hash"] = "genesis",
                ["Sync:Worker:ExitOnCompletion"] = "false"
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
        
        // Use reflection to access internal methods
        var workerType = typeof(CardanoIndexWorker<TestDbContext>);
        
        // Build dependency graph
        var buildGraphMethod = workerType.GetMethod("BuildDependencyGraph", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        buildGraphMethod!.Invoke(worker, null);
        
        // Initialize all reducer states
        var initMethod = workerType.GetMethod("InitializeAllReducerStatesAsync", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var cts = new CancellationTokenSource();
        await (Task)initMethod!.Invoke(worker, [cts.Token])!;
        
        // Get the safe intersection points for BlockTestReducer
        var getSafeIntersectionMethod = workerType.GetMethod("GetSafeIntersectionPoints", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var intersections = (IEnumerable<Point>)getSafeIntersectionMethod!.Invoke(worker, ["BlockTestReducer"])!;
        
        var safeIntersection = intersections.FirstOrDefault();
        
        _output.WriteLine($"BlockTestReducer latest: slot 1000");
        _output.WriteLine($"DependentTransactionReducer latest: slot 995");
        _output.WriteLine($"ChainedDependentReducer latest: slot 990");
        _output.WriteLine($"Safe intersection point: slot {safeIntersection?.Slot} (hash: {safeIntersection?.Hash})");
        
        // Assert - Should use the oldest intersection (slot 990) from ChainedDependentReducer
        Assert.NotNull(safeIntersection);
        Assert.Equal(990UL, safeIntersection.Slot);
        Assert.Equal("hash990", safeIntersection.Hash);
        
        // Verify that dependent reducers still use their own intersections
        var dependentIntersections = (IEnumerable<Point>)getSafeIntersectionMethod!.Invoke(worker, ["DependentTransactionReducer"])!;
        var dependentLatest = dependentIntersections.OrderByDescending(p => p.Slot).FirstOrDefault();
        Assert.Equal(995UL, dependentLatest?.Slot);
    }
    
    [Fact]
    public async Task SafeIntersection_ShouldHandleRootReducerWithoutDependents()
    {
        // Setup
        var dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using var dbContext = await dbContextFactory.CreateDbContextAsync();
        
        // Create a root reducer state (TransactionTestReducer has no dependents)
        var txReducerState = new ReducerState("TransactionTestReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections =
            [
                new Point("hash1100", 1100),
                new Point("hash1200", 1200),
                new Point("hash1300", 1300)
            ]
        };
        
        dbContext.ReducerStates.Add(txReducerState);
        await dbContext.SaveChangesAsync();
        
        // Create reducer
        var txReducer = new TransactionTestReducer(dbContextFactory);
        var reducers = new List<IReducer<IReducerModel>> { txReducer };
        
        // Create configuration
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
        
        // Use reflection to test
        var workerType = typeof(CardanoIndexWorker<TestDbContext>);
        
        // Build dependency graph
        var buildGraphMethod = workerType.GetMethod("BuildDependencyGraph", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        buildGraphMethod!.Invoke(worker, null);
        
        // Initialize states
        var initMethod = workerType.GetMethod("InitializeAllReducerStatesAsync", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var cts = new CancellationTokenSource();
        await (Task)initMethod!.Invoke(worker, [cts.Token])!;
        
        // Get the safe intersection points
        var getSafeIntersectionMethod = workerType.GetMethod("GetSafeIntersectionPoints", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        var intersections = (IEnumerable<Point>)getSafeIntersectionMethod!.Invoke(worker, ["TransactionTestReducer"])!;
        
        var latestIntersection = intersections.OrderByDescending(p => p.Slot).FirstOrDefault();
        
        _output.WriteLine($"TransactionTestReducer uses its own latest: slot {latestIntersection?.Slot}");
        
        // Assert - Root reducer without dependents should use its own intersections
        Assert.NotNull(latestIntersection);
        Assert.Equal(1300UL, latestIntersection.Slot);
        Assert.Equal("hash1300", latestIntersection.Hash);
    }
}