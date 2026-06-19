using System.Globalization;
using Argus.Sync.Data;
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
/// End-to-end crash-recovery for a fork dependent (backlog P1-5), driven by the real watched preprod
/// blocks. Topology: balance reducer (root) → { survivor snapshot, crasher snapshot }.
/// <para>Phase 1 — a worker runs with the crasher armed; it throws once on a mid-sequence block. The
/// worker fails fast, so the crasher's checkpoint lags the parent/sibling (its branch rolled back).</para>
/// <para>Phase 2 — a fresh worker runs with the crasher disarmed (a transient fault). It reconnects at
/// the crasher's lagging checkpoint, rolls back to it, and replays — the crasher catches up and its
/// snapshots match the on-chain oracle at every slot, consistent with the rest of the fork.</para>
/// A logic-bug (deterministic) crash would instead crash-loop; this models a transient fault that
/// succeeds on replay. Requires a synced preprod node (skips otherwise).
/// </summary>
public sealed class ForkDependentCrashRecoveryTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
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

    private const int CrashIndex = 6; // the crasher throws on Oracle[6]

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
    public async Task ForkDependentCrashes_ThenRecoversAndCatchesUp_OnRestart()
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

        string tempDir = Path.Combine(Path.GetTempPath(), $"argus-recover-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(Path.Combine(tempDir, "Blocks"));
        foreach ((ulong slot, byte[] bytes) in rawBlocks)
        {
            await File.WriteAllBytesAsync(Path.Combine(tempDir, "Blocks", $"{slot}.cbor"), bytes);
        }

        IBlock firstBlock = ArgusUtil.DeserializeBlockWithEra(rawBlocks[rawBlocks.Keys.Min()])!;
        ulong crashSlot = Oracle[CrashIndex].Slot;
        ulong lastGoodSlot = Oracle[CrashIndex - 1].Slot;

        IDbContextFactory<TestDbContext> dbf = _db!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        ILogger<CardanoIndexWorker<TestDbContext>> logger = loggerFactory.CreateLogger<CardanoIndexWorker<TestDbContext>>();
        IConfiguration config = BuildConfig(firstBlock);
        IReducerStateStore stateStore = new EfReducerStateStore<TestDbContext>(dbf);
        IBlockUnitOfWorkFactory uowFactory = new EfBlockUnitOfWorkFactory<TestDbContext>(dbf);

        try
        {
            // ---- Phase 1: the crasher (armed) throws on crashSlot; the worker fails fast. ----
            MockChainProviderFactory crashFactory = new(tempDir);
            List<IReducer> crashReducers =
            [
                new LovelaceBalanceByAddressReducer(config),
                new WatchedAddressBalanceReducer(config),
                new CrashOnceBalanceReducer(config, crashSlot, armed: true),
            ];
            using (CardanoIndexWorker<TestDbContext> crashWorker = new(config, logger, stateStore, uowFactory, crashReducers, crashFactory))
            {
                await crashWorker.StartAsync(CancellationToken.None);
                MockChainSyncProvider crashProvider = await WaitForProviderAsync(crashFactory);
                await Task.Delay(500);

                // Process cleanly up to the block before the crash; confirm the crasher commits each.
                for (int i = 0; i < CrashIndex; i++)
                {
                    await crashProvider.TriggerRollForwardAsync(Oracle[i].Slot);
                    ulong slot = Oracle[i].Slot;
                    await WaitForAsync(async () => await BalanceAsync(dbf, nameof(CrashOnceBalanceReducer), "A", slot) is not null,
                        TimeSpan.FromSeconds(15), $"crasher did not commit slot {slot}");
                }

                // The crash block fails the crasher's branch and the worker.
                await crashProvider.TriggerRollForwardAsync(crashSlot);
                Task crashTask = crashWorker.ExecuteTask ?? Task.CompletedTask;
                Task winner = await Task.WhenAny(crashTask, Task.Delay(TimeSpan.FromSeconds(15)));
                Assert.True(winner == crashTask, "worker did not fail fast after the fork child crashed");
                Assert.False(crashTask.IsCompletedSuccessfully, "the crash was swallowed — it must surface");

                // The crasher lags: it committed the prior block but not the crash block.
                _ = Assert.NotNull(await BalanceAsync(dbf, nameof(CrashOnceBalanceReducer), "A", lastGoodSlot));
                Assert.Null(await BalanceAsync(dbf, nameof(CrashOnceBalanceReducer), "A", crashSlot));
                try { await crashWorker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8)); } catch { /* faulted */ }
                _output.WriteLine($"Phase 1: crasher faulted on slot {crashSlot}; its checkpoint lags at {lastGoodSlot}.");
            }

            // ---- Phase 2: restart with the crasher disarmed; reconnect at its checkpoint and catch up. ----
            MockChainProviderFactory recoverFactory = new(tempDir, initialRollbackSlot: lastGoodSlot);
            List<IReducer> recoverReducers =
            [
                new LovelaceBalanceByAddressReducer(config),
                new WatchedAddressBalanceReducer(config),
                new CrashOnceBalanceReducer(config, crashSlot, armed: false),
            ];
            using CardanoIndexWorker<TestDbContext> recoverWorker = new(config, logger, stateStore, uowFactory, recoverReducers, recoverFactory);
            try
            {
                await recoverWorker.StartAsync(CancellationToken.None);
                MockChainSyncProvider recoverProvider = await WaitForProviderAsync(recoverFactory);
                await Task.Delay(500); // reconnect rollback to lastGoodSlot settles

                // Replay from the crash block onward; the disarmed crasher now catches up.
                for (int i = CrashIndex; i < Oracle.Length; i++)
                {
                    await recoverProvider.TriggerRollForwardAsync(Oracle[i].Slot);
                }
                await WaitForAsync(async () => await BalanceAsync(dbf, nameof(CrashOnceBalanceReducer), "A", Oracle[^1].Slot) is not null,
                    TimeSpan.FromSeconds(20), "crasher did not catch up to the latest slot after restart");

                // Recovered: the crasher's snapshots match the oracle at EVERY slot, like its sibling.
                foreach ((ulong slot, ulong oracleA, ulong oracleB) in Oracle)
                {
                    Assert.Equal(oracleA, await BalanceAsync(dbf, nameof(CrashOnceBalanceReducer), "A", slot));
                    Assert.Equal(oracleB, await BalanceAsync(dbf, nameof(CrashOnceBalanceReducer), "B", slot));
                    Assert.Equal(oracleA, await BalanceAsync(dbf, nameof(WatchedAddressBalanceReducer), "A", slot));
                }
                _output.WriteLine($"Phase 2: crasher recovered and caught up — matched the oracle across all {Oracle.Length} slots.");
            }
            finally
            {
                foreach (MockChainSyncProvider p in recoverFactory.CreatedProviders)
                {
                    p.CompleteChainSync();
                }
                try { await recoverWorker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8)); } catch { /* shutdown */ }
            }
        }
        finally
        {
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
                continue;
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
