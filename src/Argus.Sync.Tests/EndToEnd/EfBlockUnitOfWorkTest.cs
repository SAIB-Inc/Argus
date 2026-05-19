using System.Globalization;
using Argus.Sync.Data.Models;
using Argus.Sync.Data.Stores;
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

[Collection("Database collection")]
public sealed class EfBlockUnitOfWorkTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private TestDatabaseManager? _databaseManager;

    public Task InitializeAsync()
    {
        _databaseManager = new TestDatabaseManager(output);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_databaseManager is not null)
        {
            await _databaseManager.DisposeAsync();
            _databaseManager = null;
        }
    }

    public void Dispose()
    {
        _databaseManager?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _databaseManager = null;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CommitAsync_ShouldCommitDataAndReducerStateInOneTransaction()
    {
        IDbContextFactory<TestDbContext> factory = DbFactory();
        EfBlockUnitOfWorkFactory<TestDbContext> uowFactory = new(factory);
        Point point = new("block-hash-100", 100);

        await using (IBlockUnitOfWork uow = await uowFactory.CreateAsync())
        {
            TestDbContext dbContext = uow.GetStorage<TestDbContext>();
            _ = dbContext.BlockTests.Add(new BlockTest("block-hash-100", 1, 100, DateTime.UtcNow));
            uow.TrackIntersection("BlockTestReducer", point);

            bool committed = await uow.CommitAsync();

            Assert.True(committed);
        }

        await using TestDbContext fresh = await factory.CreateDbContextAsync();
        Assert.True(await fresh.BlockTests.AnyAsync(b => b.Hash == "block-hash-100"));

        ReducerState state = await fresh.ReducerStates.SingleAsync(r => r.Name == "BlockTestReducer");
        Assert.Equal(100UL, state.LatestIntersections.Single().Slot);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task RollbackAsync_ShouldRollbackRawSqlInsideUowTransaction()
    {
        IDbContextFactory<TestDbContext> factory = DbFactory();
        EfBlockUnitOfWorkFactory<TestDbContext> uowFactory = new(factory);

        await using (IBlockUnitOfWork uow = await uowFactory.CreateAsync())
        {
            TestDbContext dbContext = uow.GetStorage<TestDbContext>();
            _ = await dbContext.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"BlockTests\" (\"Hash\", \"Height\", \"Slot\", \"CreatedAt\") VALUES ({0}, {1}, {2}, {3})",
                "raw-rollback-hash",
                1UL,
                200UL,
                DateTime.UtcNow);
            uow.MarkDataChanged();

            await uow.RollbackAsync();
        }

        await using TestDbContext fresh = await factory.CreateDbContextAsync();
        Assert.False(await fresh.BlockTests.AnyAsync(b => b.Hash == "raw-rollback-hash"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task TrackRollback_ShouldPersistCheckpointRewindOnCommit()
    {
        IDbContextFactory<TestDbContext> factory = DbFactory();

        await using (TestDbContext seed = await factory.CreateDbContextAsync())
        {
            _ = seed.ReducerStates.Add(new ReducerState("RollbackReducer", DateTimeOffset.UtcNow)
            {
                StartIntersection = new Point("h10", 10),
                LatestIntersections =
                [
                    new Point("h30", 30),
                    new Point("h20", 20),
                    new Point("h10", 10),
                ],
            });
            _ = await seed.SaveChangesAsync();
        }

        EfBlockUnitOfWorkFactory<TestDbContext> uowFactory = new(factory);
        await using (IBlockUnitOfWork uow = await uowFactory.CreateAsync())
        {
            uow.TrackRollback("RollbackReducer", 25);
            bool committed = await uow.CommitAsync();

            Assert.True(committed);
        }

        await using TestDbContext fresh = await factory.CreateDbContextAsync();
        ReducerState state = await fresh.ReducerStates.SingleAsync(r => r.Name == "RollbackReducer");
        List<ulong> slots = [.. state.LatestIntersections.Select(p => p.Slot)];
        Assert.DoesNotContain(30UL, slots);
        Assert.Contains(20UL, slots);
        Assert.Contains(10UL, slots);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CommitAsync_WithDeferredEmptyBlock_ShouldSkipStateWrite()
    {
        IDbContextFactory<TestDbContext> factory = DbFactory();
        EfBlockUnitOfWorkFactory<TestDbContext> uowFactory = new(factory);

        await using (IBlockUnitOfWork uow = await uowFactory.CreateAsync())
        {
            uow.TrackIntersection("NoopReducer", new Point("empty-hash", 300));

            bool committed = await uow.CommitAsync(deferIfEmpty: true);

            Assert.False(committed);
        }

        await using TestDbContext fresh = await factory.CreateDbContextAsync();
        Assert.False(await fresh.ReducerStates.AnyAsync(r => r.Name == "NoopReducer"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CommitAsync_WithMarkedDataChange_ShouldCommitDeferredState()
    {
        IDbContextFactory<TestDbContext> factory = DbFactory();
        EfBlockUnitOfWorkFactory<TestDbContext> uowFactory = new(factory);

        await using (IBlockUnitOfWork uow = await uowFactory.CreateAsync())
        {
            uow.TrackIntersection("RawReducer", new Point("raw-hash", 400));
            uow.MarkDataChanged();

            bool committed = await uow.CommitAsync(deferIfEmpty: true);

            Assert.True(committed);
        }

        await using TestDbContext fresh = await factory.CreateDbContextAsync();
        ReducerState state = await fresh.ReducerStates.SingleAsync(r => r.Name == "RawReducer");
        Assert.Equal(400UL, state.LatestIntersections.Single().Slot);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PipelineLazyCommit_ShouldPersistCurrentCheckpointAfterDeferredNoOp()
    {
        string testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            output.WriteLine("Skipping test - no block test data found in TestData/Blocks/");
            return;
        }

        MockChainSyncProvider tempProvider = new(testDataDir);
        IBlock[] blocks = [.. tempProvider.AvailableBlocks.Take(2)];
        if (blocks.Length < 2)
        {
            output.WriteLine("Skipping test - at least two block test fixtures are required.");
            return;
        }

        ulong firstSlot = blocks[0].Header().HeaderBody().Slot();
        ulong secondSlot = blocks[1].Header().HeaderBody().Slot();
        MockChainProviderFactory chainProviderFactory = new(testDataDir);

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CardanoContext"] = _databaseManager!.DbContext.Database.GetConnectionString(),
            ["CardanoNodeConnection:Hash"] = blocks[0].Header().Hash(),
            ["CardanoNodeConnection:Slot"] = firstSlot.ToString(CultureInfo.InvariantCulture),
            ["CardanoNodeConnection:NetworkMagic"] = "764824073",
            ["Sync:Worker:ExitOnCompletion"] = "false",
            ["Sync:Dashboard:TuiMode"] = "false",
            ["Sync:Pipeline:ChannelCapacity"] = "8",
        }).Build();

        IDbContextFactory<TestDbContext> factory = DbFactory();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ILogger<CardanoIndexWorker<TestDbContext>> logger = loggerFactory.CreateLogger<CardanoIndexWorker<TestDbContext>>();
        EfReducerStateStore<TestDbContext> stateStore = new(factory);
        EfBlockUnitOfWorkFactory<TestDbContext> uowFactory = new(factory);
        SparseBlockReducer reducer = new(secondSlot);

        using CardanoIndexWorker<TestDbContext> worker = new(
            configuration,
            logger,
            stateStore,
            uowFactory,
            [reducer],
            chainProviderFactory);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForAsync(() => Task.FromResult(chainProviderFactory.CreatedProviders.Count == 1), TimeSpan.FromSeconds(5));
            MockChainSyncProvider provider = chainProviderFactory.CreatedProviders[0];

            await provider.TriggerRollForwardAsync(firstSlot);
            await provider.TriggerRollForwardAsync(secondSlot);

            await WaitForAsync(async () =>
            {
                await using TestDbContext fresh = await factory.CreateDbContextAsync();
                ReducerState? state = await fresh.ReducerStates
                    .AsNoTracking()
                    .SingleOrDefaultAsync(r => r.Name == nameof(SparseBlockReducer));
                return state?.LatestIntersections.SingleOrDefault()?.Slot == secondSlot;
            }, TimeSpan.FromSeconds(10));

            await using TestDbContext verify = await factory.CreateDbContextAsync();
            ReducerState state = await verify.ReducerStates.SingleAsync(r => r.Name == nameof(SparseBlockReducer));
            Assert.Equal(secondSlot, state.LatestIntersections.Single().Slot);
            Assert.False(await verify.BlockTests.AnyAsync(b => b.Slot == firstSlot));
            Assert.True(await verify.BlockTests.AnyAsync(b => b.Slot == secondSlot));
        }
        finally
        {
            if (chainProviderFactory.CreatedProviders.Count > 0)
            {
                chainProviderFactory.CreatedProviders[0].CompleteChainSync();
            }
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private IDbContextFactory<TestDbContext> DbFactory()
        => _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        Assert.Fail($"Condition was not met within {timeout}.");
    }

    private sealed class SparseBlockReducer(ulong writeSlot) : IReducer
    {
        public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
        {
            TestDbContext dbContext = uow.GetStorage<TestDbContext>();
            dbContext.BlockTests.RemoveRange(dbContext.BlockTests.Where(b => b.Slot >= slot));
            return Task.CompletedTask;
        }

        public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
        {
            ulong slot = block.Header().HeaderBody().Slot();
            if (slot == writeSlot)
            {
                TestDbContext dbContext = uow.GetStorage<TestDbContext>();
                _ = dbContext.BlockTests.Add(new BlockTest(block.Header().Hash(), block.Header().HeaderBody().BlockNumber(), slot, DateTime.UtcNow));
            }

            return Task.CompletedTask;
        }
    }
}
