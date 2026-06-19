using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

/// <summary>
/// Fork dependent of <see cref="LovelaceBalanceByAddressReducer"/>: each block, derives every
/// watched address's unspent balance from the parent's <see cref="WalletUtxo"/> rows and records a
/// per-slot snapshot. Demonstrates a dependent consuming its parent's data. In a fork the parent
/// commits its unit-of-work before forking, and each child gets a fresh empty change-tracker — so
/// a dependent reads the parent's fully-committed data straight from the database. The reusable
/// <see cref="WriteSnapshotAsync"/>/<see cref="RemoveSnapshotsAsync"/> helpers are shared with the
/// fork sibling so both children run identical logic under their own name.
/// </summary>
[DependsOn(typeof(LovelaceBalanceByAddressReducer))]
public class WatchedAddressBalanceReducer(IConfiguration configuration) : IReducer
{
    private readonly IReadOnlyList<(string Name, string Bech32)> _watched = ReadWatched(configuration);

    /// <inheritdoc />
    public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(block);
        ulong slot = block.Header().HeaderBody().Slot();
        return WriteSnapshotAsync(uow, nameof(WatchedAddressBalanceReducer), _watched, slot, ct);
    }

    /// <inheritdoc />
    public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
        => RemoveSnapshotsAsync(uow, nameof(WatchedAddressBalanceReducer), slot, ct);

    /// <summary>Reads the watched (name, bech32) set from <c>Example:WatchedAddresses</c>.</summary>
    internal static IReadOnlyList<(string Name, string Bech32)> ReadWatched(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return
        [
            .. configuration.GetSection("Example:WatchedAddresses").GetChildren()
                .Where(c => !string.IsNullOrWhiteSpace(c["Name"]))
                .Select(c => (c["Name"]!, c["Bech32"] ?? c["Name"]!))
        ];
    }

    /// <summary>
    /// Derives each watched address's unspent balance from the parent's <see cref="WalletUtxo"/>
    /// rows and writes one snapshot row per watched address, tagged with <paramref name="reducerName"/>.
    /// </summary>
    public static async Task WriteSnapshotAsync(
        IBlockUnitOfWork uow,
        string reducerName,
        IReadOnlyList<(string Name, string Bech32)> watched,
        ulong slot,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(watched);
        TestDbContext ctx = uow.GetStorage<TestDbContext>();

        // Fork child: read the parent's committed rows from the DB (the parent commits before
        // forking, and this child's change-tracker starts empty). Sum in memory to avoid EF
        // translating SUM over an unsigned column.
        List<WalletUtxo> live = await ctx.WalletUtxos.Where(u => u.SpentSlot == null).ToListAsync(ct);
        Dictionary<string, ulong> balanceByName = live
            .GroupBy(u => u.AddressName)
            .ToDictionary(g => g.Key, g => g.Aggregate(0UL, (sum, u) => sum + u.Amount));

        foreach ((string name, string bech32) in watched)
        {
            _ = ctx.WatchedAddressBalances.Add(new WatchedAddressBalanceSnapshot
            {
                Reducer = reducerName,
                AddressName = name,
                Address = bech32,
                Slot = slot,
                Balance = balanceByName.GetValueOrDefault(name, 0UL),
            });
        }
    }

    /// <summary>Removes this reducer's snapshot rows at or after <paramref name="slot"/>.</summary>
    public static async Task RemoveSnapshotsAsync(IBlockUnitOfWork uow, string reducerName, ulong slot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        TestDbContext ctx = uow.GetStorage<TestDbContext>();
        List<WatchedAddressBalanceSnapshot> stale = await ctx.WatchedAddressBalances
            .Where(s => s.Reducer == reducerName && s.Slot >= slot)
            .ToListAsync(ct);
        ctx.WatchedAddressBalances.RemoveRange(stale);
    }
}
