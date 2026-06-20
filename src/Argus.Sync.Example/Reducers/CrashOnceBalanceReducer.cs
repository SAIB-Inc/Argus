using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;

namespace Argus.Sync.Example.Reducers;

/// <summary>
/// Fork dependent of <see cref="LovelaceBalanceByAddressReducer"/> that behaves like
/// <see cref="WatchedAddressBalanceReducer"/> (writes a per-slot balance snapshot derived from the
/// parent's data) but throws once on <paramref name="crashSlot"/> while <paramref name="armed"/>.
/// Used to test transient-crash recovery: a worker built with it armed fails fast at that slot; a
/// restarted worker built with it disarmed replays from the reducer's own checkpoint and the
/// snapshots catch up to the rest of the fork.
/// </summary>
[DependsOn(typeof(LovelaceBalanceByAddressReducer))]
public class CrashOnceBalanceReducer(IConfiguration configuration, ulong crashSlot, bool armed) : IReducer
{
    private readonly IReadOnlyList<(string Name, string Bech32)> _watched = WatchedAddressBalanceReducer.ReadWatched(configuration);

    /// <inheritdoc />
    public async Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(block);
        ulong slot = block.Header().HeaderBody().Slot();
        if (armed && slot == crashSlot)
        {
            throw new InvalidOperationException($"Intentional transient crash at slot {slot}");
        }
        await WatchedAddressBalanceReducer.WriteSnapshotAsync(uow, nameof(CrashOnceBalanceReducer), _watched, slot, ct);
    }

    /// <inheritdoc />
    public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
        => WatchedAddressBalanceReducer.RemoveSnapshotsAsync(uow, nameof(CrashOnceBalanceReducer), slot, ct);
}
