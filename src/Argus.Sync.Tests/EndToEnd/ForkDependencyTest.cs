using System.Globalization;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Data.Stores;
using Argus.Sync.Example.Data;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Reducers;
using Argus.Sync.Tests.Infrastructure;
using Argus.Sync.Tests.Mocks;
using Argus.Sync.Utils;
using Argus.Sync.Workers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Fork-point integration test (backlog P1-5). Builds a 1-parent → 2-dependent fork:
/// <c>LovelaceBalanceByAddressReducer</c> (root) feeds two siblings — <see cref="WatchedAddressBalanceReducer"/>
/// and <see cref="WatchedAddressBalanceSiblingReducer"/> — that each derive the watched balances from the
/// parent's committed <c>WalletUtxo</c> rows. Driving the real watched blocks through the worker exercises
/// the actual fork forwarding (parent commits, then spawns a fresh unit-of-work per child). Asserting both
/// children match the on-chain oracle at every slot proves each independently reads the parent's data with
/// its own empty change-tracker; a rollback then proves the cascade reaches both. Requires a synced preprod
/// node (skips otherwise).
/// </summary>
public sealed class ForkDependencyTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private const string SocketEnvVar = "CARDANO_NODE_SOCKET_PATH";
    private const string DefaultSocketPath = "/home/rawriclark/CardanoPreprod/ipc/node.socket";
    private const ulong NetworkMagic = 1UL; // preprod

    private const ulong IntersectionSlot = 126025608UL;
    private const string IntersectionHash = "7ef942e6a670af6310737e9230b22e11a4bb1af69bed9affb09b1025b371d1cd";

    private static readonly (string Name, string Bech32, string Hex)[] WatchedAddresses =
    [
        ("A", "addr_test1vpnu2d2qgfkgwesa3cr8mv69fce2uvrmlpnwgs5m724l9zcutgtgu", "6067c53540426c87661d8e067db3454e32ae307bf866e4429bf2abf28b"),
        ("B", "addr_test1vr6mkntzk74tldx4u5ra599fl5539rka8uzh3cxup2q9x2sn59c7g", "60f5bb4d62b7aabfb4d5e507da14a9fd29128edd3f0578e0dc0a80532a"),
    ];

    // The oracle: unspent balance (lovelace) of A and B as of each slot where a balance changed.
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
    private static readonly string[] DependentNames =
        [nameof(WatchedAddressBalanceReducer), nameof(WatchedAddressBalanceSiblingReducer)];

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
    public async Task ForkedDependents_BothDeriveParentBalance_AndCascadeOnRollback()
    {
        string socketPath = Environment.GetEnvironmentVariable(SocketEnvVar) ?? DefaultSocketPath;
        if (!File.Exists(socketPath))
        {
            _output.WriteLine($"SKIP: node socket '{socketPath}' not found.");
            return;
        }

        Dictionary<ulong, byte[]> rawBlocks = await FetchWatchedBlockBytesAsync(socketPath);
        if (rawBlocks.Count != Oracle.Length)
        {
            _output.WriteLine($"SKIP: fetched {rawBlocks.Count}/{Oracle.Length} watched blocks — preprod history differs.");
            return;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), $"argus-fork-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(Path.Combine(tempDir, "Blocks"));
        foreach ((ulong slot, byte[] bytes) in rawBlocks)
        {
            await File.WriteAllBytesAsync(Path.Combine(tempDir, "Blocks", $"{slot}.cbor"), bytes);
        }

        ulong firstSlot = rawBlocks.Keys.Min();
        IBlock firstBlock = ArgusUtil.DeserializeBlockWithEra(rawBlocks[firstSlot])!;

        IDbContextFactory<TestDbContext> dbf = _db!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        ILogger<CardanoIndexWorker<TestDbContext>> logger = loggerFactory.CreateLogger<CardanoIndexWorker<TestDbContext>>();

        IConfiguration config = BuildConfig(firstBlock);
        IReducerStateStore stateStore = new EfReducerStateStore<TestDbContext>(dbf);
        IBlockUnitOfWorkFactory uowFactory = new EfBlockUnitOfWorkFactory<TestDbContext>(dbf);

        // The fork: balance reducer (root) → two snapshot dependents.
        List<IReducer> reducers =
        [
            new LovelaceBalanceByAddressReducer(config),
            new WatchedAddressBalanceReducer(config),
            new WatchedAddressBalanceSiblingReducer(config),
        ];

        MockChainProviderFactory factory = new(tempDir);
        using CardanoIndexWorker<TestDbContext> worker = new(config, logger, stateStore, uowFactory, reducers, factory);

        try
        {
            await worker.StartAsync(CancellationToken.None);
            MockChainSyncProvider provider = await WaitForProviderAsync(factory);
            await Task.Delay(500); // let the initial intersection rollback settle

            // Forward pass: drive every watched block; both fork children must match the oracle.
            foreach ((ulong slot, ulong oracleA, ulong oracleB) in Oracle)
            {
                await provider.TriggerRollForwardAsync(slot);
                await WaitForAsync(
                    async () => await BothDependentsSnapshottedAsync(dbf, slot),
                    TimeSpan.FromSeconds(15),
                    $"both fork dependents did not snapshot slot {slot}");

                foreach (string dependent in DependentNames)
                {
                    Assert.Equal(oracleA, await BalanceAsync(dbf, dependent, "A", slot));
                    Assert.Equal(oracleB, await BalanceAsync(dbf, dependent, "B", slot));
                }
            }
            _output.WriteLine($"Forward: both fork dependents matched the oracle across all {Oracle.Length} slots.");

            // Only the root reducer gets a chain connection; dependents are fed by forwarding.
            _ = Assert.Single(factory.CreatedProviders);

            // Rollback: exclusive to a mid slot → removes the last RollbackChanges from BOTH children.
            int keepThrough = Oracle.Length - RollbackChanges; // keep Oracle[0..keepThrough-1]
            (ulong keptSlot, ulong keptA, ulong keptB) = Oracle[keepThrough - 1];
            ulong removedSlot = Oracle[keepThrough].Slot;

            await provider.TriggerRollBackAsync(keptSlot, RollBackType.Exclusive);
            await WaitForAsync(
                async () => !await AnySnapshotAtOrAfterAsync(dbf, removedSlot),
                TimeSpan.FromSeconds(15),
                "rollback did not cascade to the fork dependents");

            foreach (string dependent in DependentNames)
            {
                // Latest surviving snapshot is the kept slot, with the kept oracle value...
                Assert.Equal(keptA, await BalanceAsync(dbf, dependent, "A", keptSlot));
                Assert.Equal(keptB, await BalanceAsync(dbf, dependent, "B", keptSlot));
                // ...and nothing beyond it survived.
                await using TestDbContext ctx = await dbf.CreateDbContextAsync();
                Assert.False(await ctx.WatchedAddressBalances.AnyAsync(s => s.Reducer == dependent && s.Slot > keptSlot));
            }
            _output.WriteLine($"Rollback cascaded to both dependents: kept through slot {keptSlot}, removed {RollbackChanges} changes.");
        }
        finally
        {
            foreach (MockChainSyncProvider p in factory.CreatedProviders)
            {
                p.CompleteChainSync();
            }
            try { await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8)); } catch { /* shutdown */ }
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    private IConfiguration BuildConfig(IBlock firstBlock)
    {
        Dictionary<string, string?> values = new()
        {
            ["ConnectionStrings:CardanoContext"] = _db!.DbContext.Database.GetConnectionString(),
            ["CardanoNodeConnection:Hash"] = firstBlock.Header().Hash(),
            ["CardanoNodeConnection:Slot"] = firstBlock.Header().HeaderBody().Slot().ToString(CultureInfo.InvariantCulture),
            ["CardanoNodeConnection:NetworkMagic"] = NetworkMagic.ToString(CultureInfo.InvariantCulture),
            ["Sync:Worker:ExitOnCompletion"] = "false",
            ["Sync:Dashboard:TuiMode"] = "false",
        };
        for (int i = 0; i < WatchedAddresses.Length; i++)
        {
            values[$"Example:WatchedAddresses:{i}:Name"] = WatchedAddresses[i].Name;
            values[$"Example:WatchedAddresses:{i}:Bech32"] = WatchedAddresses[i].Bech32;
            values[$"Example:WatchedAddresses:{i}:Hex"] = WatchedAddresses[i].Hex;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static async Task<ulong?> BalanceAsync(IDbContextFactory<TestDbContext> dbf, string reducer, string addressName, ulong slot)
    {
        await using TestDbContext ctx = await dbf.CreateDbContextAsync();
        return await ctx.WatchedAddressBalances.AsNoTracking()
            .Where(s => s.Reducer == reducer && s.AddressName == addressName && s.Slot == slot)
            .Select(s => (ulong?)s.Balance)
            .SingleOrDefaultAsync();
    }

    private static async Task<bool> BothDependentsSnapshottedAsync(IDbContextFactory<TestDbContext> dbf, ulong slot)
    {
        await using TestDbContext ctx = await dbf.CreateDbContextAsync();
        foreach (string dependent in DependentNames)
        {
            if (!await ctx.WatchedAddressBalances.AsNoTracking().AnyAsync(s => s.Reducer == dependent && s.Slot == slot))
            {
                return false;
            }
        }
        return true;
    }

    private static async Task<bool> AnySnapshotAtOrAfterAsync(IDbContextFactory<TestDbContext> dbf, ulong slot)
    {
        await using TestDbContext ctx = await dbf.CreateDbContextAsync();
        return await ctx.WatchedAddressBalances.AsNoTracking().AnyAsync(s => s.Slot >= slot);
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

    /// <summary>Fetches the raw era-tagged CBOR for each watched block over N2C, keyed by slot.</summary>
    private static async Task<Dictionary<ulong, byte[]>> FetchWatchedBlockBytesAsync(string socketPath)
    {
        HashSet<ulong> wantedSlots = [.. Oracle.Select(o => o.Slot)];
        ulong lastSlot = Oracle[^1].Slot;
        Dictionary<ulong, byte[]> collected = [];

        NodeClient client = await NodeClient.ConnectAsync(socketPath, CancellationToken.None);
        await client.StartAsync(NetworkMagic);

        SpecificPoint start = new(IntersectionSlot, Convert.FromHexString(IntersectionHash));
        ChainSyncMessage intersection = await client.ChainSync!.FindIntersectionAsync([start], CancellationToken.None);
        if (intersection is not MessageIntersectFound)
        {
            return collected;
        }

        for (int guard = 0; guard < 5000 && collected.Count < wantedSlots.Count; guard++)
        {
            MessageNextResponse? next = await client.ChainSync.NextRequestAsync(CancellationToken.None);
            if (next is not N2CMessageRollForward rollForward)
            {
                continue; // initial RollBackward to the intersection, etc.
            }

            byte[] bytes = rollForward.Payload.Value.ToArray();
            IBlock? block = ArgusUtil.DeserializeBlockWithEra(bytes);
            if (block is null)
            {
                continue;
            }

            ulong slot = block.Header().HeaderBody().Slot();
            if (wantedSlots.Contains(slot))
            {
                collected[slot] = bytes;
            }

            if (slot > lastSlot)
            {
                break;
            }
        }

        return collected;
    }
}
