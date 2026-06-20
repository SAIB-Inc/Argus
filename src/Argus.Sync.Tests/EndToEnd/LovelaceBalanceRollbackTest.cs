using Argus.Sync.Data.Models;
using Argus.Sync.EntityFramework;
using Argus.Sync.Example.Data;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Tests.Infrastructure;
using Argus.Sync.Utils;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// End-to-end rollback test for <see cref="LovelaceBalanceByAddressReducer"/>.
///
/// The transactions used here happened at fixed, known preprod slots and are now
/// immutable chain history, so the test fetches those exact blocks LIVE from the node
/// each run (deterministic, because confirmed blocks never change). It then:
///   1. processes every watched block forward, asserting the known balance at each slot,
///   2. rolls back the last N balance-changes via <c>RollBackwardAsync</c>,
///   3. replays the undone blocks one at a time, asserting the balance is accurate at
///      every single step — exercising the un-create (delete) and un-spend (resurrect)
///      paths and proving rollback+replay reproduces the chain exactly.
///
/// Requires a synced preprod node carrying this history (skips gracefully otherwise).
/// </summary>
public sealed class LovelaceBalanceRollbackTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private const string SocketEnvVar = "CARDANO_NODE_SOCKET_PATH";
    private const string DefaultSocketPath = "/home/rawriclark/CardanoPreprod/ipc/node.socket";
    private const ulong NetworkMagic = 1UL; // preprod

    // A real block immediately before our first watched transaction — the chain-sync
    // intersection to start streaming from.
    private const ulong IntersectionSlot = 126025608UL;
    private const string IntersectionHash = "7ef942e6a670af6310737e9230b22e11a4bb1af69bed9affb09b1025b371d1cd";

    // N2N TCP endpoint of the same preprod node (for the N2N variant of this test).
    private const string N2NHost = "localhost";
    private const int N2NPort = 3001;

    // Watched addresses: friendly name, bech32 (for display), raw base16 (for matching).
    private static readonly (string Name, string Bech32, string Hex)[] WatchedAddresses =
    [
        ("A", "addr_test1vpnu2d2qgfkgwesa3cr8mv69fce2uvrmlpnwgs5m724l9zcutgtgu", "6067c53540426c87661d8e067db3454e32ae307bf866e4429bf2abf28b"),
        ("B", "addr_test1vr6mkntzk74tldx4u5ra599fl5539rka8uzh3cxup2q9x2sn59c7g", "60f5bb4d62b7aabfb4d5e507da14a9fd29128edd3f0578e0dc0a80532a"),
    ];

    // The oracle: balance (lovelace) of A and B as of each slot where a balance changed.
    // Independently derived from the on-chain UTxO set; every replay step must match.
    private static readonly (ulong Slot, ulong A, ulong B)[] Oracle =
    [
        (126025649UL, 10000000000UL, 0UL),          // 10,000 ADA deposit -> A
        (126027218UL, 8499829747UL, 1500000000UL),  // A -> B 1,500
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

    private const int RollbackChanges = 5; // roll back the last 5 balance-changes

    private readonly ITestOutputHelper _output = output;
    private TestDatabaseManager? _db;

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
    public async Task RollbackThenStepForward_TracksBalanceAccuratelyAtEveryStep()
    {
        string socketPath = Environment.GetEnvironmentVariable(SocketEnvVar) ?? DefaultSocketPath;
        if (!File.Exists(socketPath))
        {
            _output.WriteLine($"SKIP: node socket '{socketPath}' not found.");
            return;
        }

        // 1. Fetch the exact watched-tx blocks live from the node.
        IReadOnlyList<IBlock> blocks = await FetchWatchedBlocksAsync(socketPath);
        if (blocks.Count != Oracle.Length)
        {
            _output.WriteLine($"SKIP: fetched {blocks.Count}/{Oracle.Length} watched blocks — preprod history differs from this fixed test.");
            return;
        }

        await RunBalanceRollbackScenarioAsync(blocks, "N2C");
    }

    /// <summary>
    /// N2N variant: fetch the exact same watched blocks over the node-to-node path (header
    /// chain-sync → BlockFetch body) and run the identical forward → rollback → replay assertions.
    /// Directly proves balance + rollback accuracy on N2N, not just N2C.
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task N2N_RollbackThenStepForward_TracksBalanceAccuratelyAtEveryStep()
    {
        if (!await IsN2NReachableAsync(N2NHost, N2NPort))
        {
            _output.WriteLine($"SKIP: N2N port {N2NHost}:{N2NPort} not reachable.");
            return;
        }

        IReadOnlyList<IBlock> blocks = await FetchWatchedBlocksViaN2NAsync(N2NHost, N2NPort);
        if (blocks.Count != Oracle.Length)
        {
            _output.WriteLine($"SKIP: fetched {blocks.Count}/{Oracle.Length} watched blocks over N2N — preprod history differs or node not synced.");
            return;
        }

        await RunBalanceRollbackScenarioAsync(blocks, "N2N");
    }

    /// <summary>
    /// Provider-independent core: drive the reducer forward over every watched block, roll back the
    /// last N balance-changes, then replay one block at a time — asserting the oracle balance at
    /// every step. Identical for N2C and N2N (both deliver the same full <see cref="IBlock"/>s).
    /// </summary>
    private async Task RunBalanceRollbackScenarioAsync(IReadOnlyList<IBlock> blocks, string mode)
    {
        LovelaceBalanceByAddressReducer reducer = new(BuildWatchConfiguration());
        IDbContextFactory<TestDbContext> factory = _db!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();

        // 1. Forward pass: build the full UTxO state, asserting the oracle after each block.
        _output.WriteLine($"=== [{mode}] forward pass (building state) ===");
        for (int i = 0; i < blocks.Count; i++)
        {
            await RollForwardAsync(reducer, factory, blocks[i]);
            await AssertBalancesAsync(Oracle[i], $"[{mode}] forward block {i} @ slot {Oracle[i].Slot}");
        }

        // 2. Roll back the last N balance-changes.
        int firstUndoneIndex = Oracle.Length - RollbackChanges; // first change to undo/replay
        ulong rollbackSlot = Oracle[firstUndoneIndex].Slot;
        (ulong Slot, ulong A, ulong B) asOfBeforeRollback = Oracle[firstUndoneIndex - 1];

        _output.WriteLine($"=== [{mode}] rollback last {RollbackChanges} changes: RollBackwardAsync({rollbackSlot}) ===");
        await RollBackwardAsync(reducer, factory, rollbackSlot);
        await AssertBalancesAsync(asOfBeforeRollback, $"[{mode}] post-rollback (as of slot {asOfBeforeRollback.Slot})");

        // 3. Controlled step-forward replay: one block at a time, assert accuracy at each.
        _output.WriteLine($"=== [{mode}] controlled step-forward replay ===");
        for (int i = firstUndoneIndex; i < blocks.Count; i++)
        {
            await RollForwardAsync(reducer, factory, blocks[i]);
            await AssertBalancesAsync(Oracle[i], $"[{mode}] replay block {i} @ slot {Oracle[i].Slot}");
        }

        // 4. Final anchor.
        await AssertBalancesAsync(Oracle[^1], $"[{mode}] final");
        _output.WriteLine($"PASS [{mode}]: rollback of {RollbackChanges} changes + step-forward replay reproduced every balance exactly.");
    }

    private static async Task<IReadOnlyList<IBlock>> FetchWatchedBlocksViaN2NAsync(string host, int port)
    {
        HashSet<ulong> wantedSlots = [.. Oracle.Select(o => o.Slot)];
        ulong lastSlot = Oracle[^1].Slot;
        Dictionary<ulong, IBlock> collected = [];

        await using N2NProvider provider = new(host, port);
        List<Data.Models.Point> intersection = [new(IntersectionHash, IntersectionSlot)];
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(60));

        await foreach (NextResponse response in provider.StartChainSyncAsync(intersection, NetworkMagic, cts.Token))
        {
            if (response.Action != NextResponseAction.RollForward)
            {
                continue; // initial RollBackward to the intersection, etc.
            }

            IBlock block = response.Block!;
            ulong slot = block.Header().HeaderBody().Slot();
            if (wantedSlots.Contains(slot))
            {
                collected[slot] = block;
            }

            if (slot > lastSlot || collected.Count == wantedSlots.Count)
            {
                break;
            }
        }

        return [.. collected.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value)];
    }

    private static async Task<bool> IsN2NReachableAsync(string host, int port)
    {
        try
        {
            using System.Net.Sockets.TcpClient client = new();
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
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
                continue; // initial RollBackward to the intersection, etc.
            }

            IBlock? block = ArgusUtil.DeserializeBlockWithEra(rollForward.Payload.Value);
            if (block == null)
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

    private static async Task RollForwardAsync(LovelaceBalanceByAddressReducer reducer, IDbContextFactory<TestDbContext> factory, IBlock block)
    {
        await using IBlockUnitOfWork uow = new EfBlockUnitOfWork<TestDbContext>(factory.CreateDbContext());
        await reducer.RollForwardAsync(block, uow, CancellationToken.None);
        _ = await uow.CommitAsync();
    }

    private static async Task RollBackwardAsync(LovelaceBalanceByAddressReducer reducer, IDbContextFactory<TestDbContext> factory, ulong slot)
    {
        await using IBlockUnitOfWork uow = new EfBlockUnitOfWork<TestDbContext>(factory.CreateDbContext());
        await reducer.RollBackwardAsync(slot, uow, CancellationToken.None);
        _ = await uow.CommitAsync();
    }

    private async Task AssertBalancesAsync((ulong Slot, ulong A, ulong B) expected, string label)
    {
        (ulong actualA, ulong actualB) = await ReadLiveBalancesAsync();
        _output.WriteLine($"  {label}: A={actualA} (expected {expected.A}), B={actualB} (expected {expected.B})");
        Assert.Equal(expected.A, actualA);
        Assert.Equal(expected.B, actualB);
    }

    private async Task<(ulong A, ulong B)> ReadLiveBalancesAsync()
    {
        await using TestDbContext ctx = _db!.ServiceProvider
            .GetRequiredService<IDbContextFactory<TestDbContext>>()
            .CreateDbContext();

        List<ulong> aAmounts = await ctx.WalletUtxos
            .Where(u => u.AddressName == "A" && u.SpentSlot == null)
            .Select(u => u.Amount)
            .ToListAsync();
        List<ulong> bAmounts = await ctx.WalletUtxos
            .Where(u => u.AddressName == "B" && u.SpentSlot == null)
            .Select(u => u.Amount)
            .ToListAsync();

        return (aAmounts.Aggregate(0UL, (sum, x) => sum + x), bAmounts.Aggregate(0UL, (sum, x) => sum + x));
    }
}
