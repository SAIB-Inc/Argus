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
using System.Reflection;

namespace Argus.Sync.Tests.EndToEnd;

[Collection("Database collection")]
public class SafeIntersectionTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
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
                ["CardanoNodeConnection:Hash"] = "genesis",
                ["Sync:Worker:ExitOnCompletion"] = "false"
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

    private static async Task BuildGraphAndInitializeAsync(
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
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SafeIntersection_ShouldUseOldestPointForRootReducerWithDependents()
    {
        // Setup
        IDbContextFactory<TestDbContext> dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        // Pre-populate reducer states with misaligned intersection points
        // This simulates what happens when reducers are stopped at different times
        ReducerState blockReducerState = new("BlockTestReducer", DateTimeOffset.UtcNow)
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
        ReducerState dependentReducerState = new("DependentTransactionReducer", DateTimeOffset.UtcNow)
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
        ReducerState chainedReducerState = new("ChainedDependentReducer", DateTimeOffset.UtcNow)
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
        _ = await dbContext.SaveChangesAsync();

        // Create reducers
        BlockTestReducer blockReducer = new(dbContextFactory);
        DependentTransactionReducer dependentReducer = new(dbContextFactory);
        ChainedDependentReducer chainedReducer = new(dbContextFactory);
        List<IReducer<IReducerModel>> reducers = [blockReducer, dependentReducer, chainedReducer];

        (CardanoIndexWorker<TestDbContext> worker, ILoggerFactory loggerFactory, CancellationTokenSource cts) = CreateWorkerWithReducers(dbContextFactory, reducers);
        using (loggerFactory)
        using (cts)
        using (worker)
        {
            await BuildGraphAndInitializeAsync(worker, cts.Token);

            // Get the safe intersection points for BlockTestReducer
            Type workerType = typeof(CardanoIndexWorker<TestDbContext>);
            MethodInfo? getSafeIntersectionMethod = workerType.GetMethod("GetSafeIntersectionPoints",
                BindingFlags.NonPublic | BindingFlags.Instance);
            IEnumerable<Point> intersections = (IEnumerable<Point>)getSafeIntersectionMethod!.Invoke(worker, ["BlockTestReducer"])!;

            Point? safeIntersection = intersections.FirstOrDefault();

            _output.WriteLine($"BlockTestReducer latest: slot 1000");
            _output.WriteLine($"DependentTransactionReducer latest: slot 995");
            _output.WriteLine($"ChainedDependentReducer latest: slot 990");
            _output.WriteLine($"Safe intersection point: slot {safeIntersection?.Slot} (hash: {safeIntersection?.Hash})");

            // Assert - Should use the oldest intersection (slot 990) from ChainedDependentReducer
            Assert.NotNull(safeIntersection);
            Assert.Equal(990UL, safeIntersection.Slot);
            Assert.Equal("hash990", safeIntersection.Hash);

            // Verify that dependent reducers still use their own intersections
            IEnumerable<Point> dependentIntersections = (IEnumerable<Point>)getSafeIntersectionMethod!.Invoke(worker, ["DependentTransactionReducer"])!;
            Point? dependentLatest = dependentIntersections.OrderByDescending(p => p.Slot).FirstOrDefault();
            Assert.Equal(995UL, dependentLatest?.Slot);
        }
    }

    [Fact]
    public async Task SafeIntersection_ShouldHandleRootReducerWithoutDependents()
    {
        // Setup
        IDbContextFactory<TestDbContext> dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        // Create a root reducer state (TransactionTestReducer has no dependents)
        ReducerState txReducerState = new("TransactionTestReducer", DateTimeOffset.UtcNow)
        {
            StartIntersection = new Point("genesis", 0),
            LatestIntersections =
            [
                new Point("hash1100", 1100),
                new Point("hash1200", 1200),
                new Point("hash1300", 1300)
            ]
        };

        _ = dbContext.ReducerStates.Add(txReducerState);
        _ = await dbContext.SaveChangesAsync();

        // Create reducer
        TransactionTestReducer txReducer = new(dbContextFactory);
        List<IReducer<IReducerModel>> reducers = [txReducer];

        (CardanoIndexWorker<TestDbContext> worker, ILoggerFactory loggerFactory, CancellationTokenSource cts) = CreateWorkerWithReducers(dbContextFactory, reducers);
        using (loggerFactory)
        using (cts)
        using (worker)
        {
            await BuildGraphAndInitializeAsync(worker, cts.Token);

            // Get the safe intersection points
            Type workerType = typeof(CardanoIndexWorker<TestDbContext>);
            MethodInfo? getSafeIntersectionMethod = workerType.GetMethod("GetSafeIntersectionPoints",
                BindingFlags.NonPublic | BindingFlags.Instance);
            IEnumerable<Point> intersections = (IEnumerable<Point>)getSafeIntersectionMethod!.Invoke(worker, ["TransactionTestReducer"])!;

            Point? latestIntersection = intersections.OrderByDescending(p => p.Slot).FirstOrDefault();

            _output.WriteLine($"TransactionTestReducer uses its own latest: slot {latestIntersection?.Slot}");

            // Assert - Root reducer without dependents should use its own intersections
            Assert.NotNull(latestIntersection);
            Assert.Equal(1300UL, latestIntersection.Slot);
            Assert.Equal("hash1300", latestIntersection.Hash);
        }
    }
}
