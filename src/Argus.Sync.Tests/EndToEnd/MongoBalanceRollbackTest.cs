using Argus.Sync.Data.Models;
using Argus.Sync.MongoDb;
using Argus.Sync.Reducers;
using Argus.Sync.Tests.Mongo;
using Argus.Sync.Utils;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Proves the storage layer is backend-agnostic by running the balance + rollback scenario on MongoDB
/// instead of EF/Postgres (backlog P2-11). A <see cref="MongoLovelaceBalanceReducer"/> indexes the same
/// watched preprod blocks through the Mongo <see cref="IBlockUnitOfWork"/>; the test drives forward →
/// rollback → replay and asserts the on-chain oracle balance at every step, plus that the reducer's
/// checkpoint (written in the same Mongo transaction) round-trips through <c>GetReducerStateAsync</c>.
/// Requires a synced preprod node AND a Mongo replica set on :27017 (skips otherwise).
/// </summary>
public sealed class MongoBalanceRollbackTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private const string SocketEnvVar = "CARDANO_NODE_SOCKET_PATH";
    private const string DefaultSocketPath = "/home/rawriclark/CardanoPreprod/ipc/node.socket";
    private const ulong NetworkMagic = 1UL; // preprod
    private const string MongoConnectionString = "mongodb://localhost:27017/?directConnection=true";

    private const ulong IntersectionSlot = 126025608UL;
    private const string IntersectionHash = "7ef942e6a670af6310737e9230b22e11a4bb1af69bed9affb09b1025b371d1cd";

    private static readonly (string Name, string Bech32, string Hex)[] WatchedAddresses =
    [
        ("A", "addr_test1vpnu2d2qgfkgwesa3cr8mv69fce2uvrmlpnwgs5m724l9zcutgtgu", "6067c53540426c87661d8e067db3454e32ae307bf866e4429bf2abf28b"),
        ("B", "addr_test1vr6mkntzk74tldx4u5ra599fl5539rka8uzh3cxup2q9x2sn59c7g", "60f5bb4d62b7aabfb4d5e507da14a9fd29128edd3f0578e0dc0a80532a"),
    ];

    private static readonly (ulong Slot, ulong A, ulong B)[] Oracle =
    [
        (126025649UL, 10000000000UL, 0UL),
        (126027218UL, 8499829747UL, 1500000000UL),
        (126027276UL, 4672680382UL, 5326979112UL),
        (126027380UL, 6881992490UL, 3117490899UL),
        (126027426UL, 5353627983UL, 4645679125UL),
        (126027492UL, 2885182806UL, 7113954225UL),
        (126027626UL, 3419721790UL, 6579232932UL),
        (126027660UL, 6126442767UL, 3872341878UL),
        (126027741UL, 3360907199UL, 6637695313UL),
        (126027869UL, 5977579664UL, 4020846743UL),
        (126027945UL, 5767263833UL, 4230986293UL),
        (126028054UL, 4275642696UL, 5722437353UL),
    ];

    private const int RollbackChanges = 5;

    private readonly ITestOutputHelper _output = output;
    private IMongoClient? _client;
    private string _dbName = string.Empty;

    public Task InitializeAsync()
    {
        _client = new MongoClient(MongoConnectionString);
        _dbName = $"argus_mongo_{Guid.NewGuid():N}";
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client is not null && !string.IsNullOrEmpty(_dbName))
        {
            try { await _client.DropDatabaseAsync(_dbName); } catch { /* best-effort */ }
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MongoBalanceReducer_RollbackThenReplay_TracksBalanceAndCheckpointAtEveryStep()
    {
        string socketPath = Environment.GetEnvironmentVariable(SocketEnvVar) ?? DefaultSocketPath;
        if (!File.Exists(socketPath))
        {
            _output.WriteLine($"SKIP: node socket '{socketPath}' not found.");
            return;
        }
        if (!await IsMongoReachableAsync())
        {
            _output.WriteLine($"SKIP: Mongo replica set not reachable at {MongoConnectionString}.");
            return;
        }

        IReadOnlyList<IBlock> blocks = await FetchWatchedBlocksAsync(socketPath);
        if (blocks.Count != Oracle.Length)
        {
            _output.WriteLine($"SKIP: fetched {blocks.Count}/{Oracle.Length} watched blocks — preprod history differs.");
            return;
        }

        IMongoDatabase db = _client!.GetDatabase(_dbName);
        MongoBlockUnitOfWorkFactory uowFactory = new(_client, _dbName);
        MongoLovelaceBalanceReducer reducer = new(BuildWatchConfiguration());
        string reducerName = nameof(MongoLovelaceBalanceReducer);

        // 1. Forward pass — build the UTxO state, asserting the oracle after each block.
        for (int i = 0; i < blocks.Count; i++)
        {
            await RollForwardAsync(reducer, uowFactory, reducerName, blocks[i]);
            await AssertBalancesAsync(db, Oracle[i], $"forward {i} @ slot {Oracle[i].Slot}");
        }

        // The checkpoint (written in the same Mongo transaction as the data) round-trips.
        ReducerState? state = await uowFactory.GetReducerStateAsync(reducerName);
        Assert.NotNull(state);
        Assert.Equal(Oracle[^1].Slot, state!.LatestIntersections.Max(p => p.Slot));

        // 2. Roll back the last N balance-changes.
        int firstUndone = Oracle.Length - RollbackChanges;
        ulong rollbackSlot = Oracle[firstUndone].Slot;
        await RollBackwardAsync(reducer, uowFactory, reducerName, rollbackSlot);
        await AssertBalancesAsync(db, Oracle[firstUndone - 1], $"post-rollback @ slot {Oracle[firstUndone - 1].Slot}");

        ReducerState? rewound = await uowFactory.GetReducerStateAsync(reducerName);
        Assert.NotNull(rewound);
        Assert.Equal(Oracle[firstUndone - 1].Slot, rewound!.LatestIntersections.Max(p => p.Slot));

        // 3. Replay the undone blocks one at a time, asserting accuracy at each.
        for (int i = firstUndone; i < blocks.Count; i++)
        {
            await RollForwardAsync(reducer, uowFactory, reducerName, blocks[i]);
            await AssertBalancesAsync(db, Oracle[i], $"replay {i} @ slot {Oracle[i].Slot}");
        }

        _output.WriteLine($"PASS [Mongo]: balance + rollback + checkpoint accurate across all {Oracle.Length} slots.");
    }

    private static async Task RollForwardAsync(MongoLovelaceBalanceReducer reducer, MongoBlockUnitOfWorkFactory uowFactory, string reducerName, IBlock block)
    {
        await using IBlockUnitOfWork uow = await uowFactory.CreateAsync();
        await reducer.RollForwardAsync(block, uow, CancellationToken.None);
        uow.TrackIntersection(reducerName, new Argus.Sync.Data.Models.Point(block.Header().Hash(), block.Header().HeaderBody().Slot()));
        _ = await uow.CommitAsync();
    }

    private static async Task RollBackwardAsync(MongoLovelaceBalanceReducer reducer, MongoBlockUnitOfWorkFactory uowFactory, string reducerName, ulong slot)
    {
        await using IBlockUnitOfWork uow = await uowFactory.CreateAsync();
        await reducer.RollBackwardAsync(slot, uow, CancellationToken.None);
        uow.TrackRollback(reducerName, slot);
        _ = await uow.CommitAsync();
    }

    private async Task AssertBalancesAsync(IMongoDatabase db, (ulong Slot, ulong A, ulong B) expected, string label)
    {
        IMongoCollection<MongoWalletUtxo> col = db.GetCollection<MongoWalletUtxo>("WalletUtxos");
        List<MongoWalletUtxo> live = await col.Find(x => x.SpentSlot == null).ToListAsync();
        ulong a = live.Where(u => u.AddressName == "A").Aggregate(0UL, (sum, u) => sum + u.Amount);
        ulong b = live.Where(u => u.AddressName == "B").Aggregate(0UL, (sum, u) => sum + u.Amount);
        _output.WriteLine($"  {label}: A={a} (expected {expected.A}), B={b} (expected {expected.B})");
        Assert.Equal(expected.A, a);
        Assert.Equal(expected.B, b);
    }

    private async Task<bool> IsMongoReachableAsync()
    {
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
            using IAsyncCursor<string> names = await _client!.ListDatabaseNamesAsync(cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IConfiguration BuildWatchConfiguration()
    {
        Dictionary<string, string?> values = [];
        for (int i = 0; i < WatchedAddresses.Length; i++)
        {
            values[$"Example:WatchedAddresses:{i}:Name"] = WatchedAddresses[i].Name;
            values[$"Example:WatchedAddresses:{i}:Bech32"] = WatchedAddresses[i].Bech32;
            values[$"Example:WatchedAddresses:{i}:Hex"] = WatchedAddresses[i].Hex;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private async Task<IReadOnlyList<IBlock>> FetchWatchedBlocksAsync(string socketPath)
    {
        HashSet<ulong> wantedSlots = [.. Oracle.Select(o => o.Slot)];
        ulong lastSlot = Oracle[^1].Slot;
        Dictionary<ulong, IBlock> collected = [];

        NodeClient client = await NodeClient.ConnectAsync(socketPath, CancellationToken.None);
        await client.StartAsync(NetworkMagic);

        SpecificPoint start = new(IntersectionSlot, Convert.FromHexString(IntersectionHash));
        ChainSyncMessage intersection = await client.ChainSync!.FindIntersectionAsync([start], CancellationToken.None);
        if (intersection is not MessageIntersectFound)
        {
            _output.WriteLine("Could not find intersection point on the node.");
            return [];
        }

        for (int guard = 0; guard < 5000 && collected.Count < wantedSlots.Count; guard++)
        {
            MessageNextResponse? next = await client.ChainSync.NextRequestAsync(CancellationToken.None);
            if (next is not N2CMessageRollForward rollForward)
            {
                continue;
            }

            IBlock? block = ArgusUtil.DeserializeBlockWithEra(rollForward.Payload.Value);
            if (block is null)
            {
                continue;
            }

            ulong slot = block.Header().HeaderBody().Slot();
            if (wantedSlots.Contains(slot))
            {
                collected[slot] = block;
            }

            if (slot > lastSlot)
            {
                break;
            }
        }

        return [.. collected.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value)];
    }
}
