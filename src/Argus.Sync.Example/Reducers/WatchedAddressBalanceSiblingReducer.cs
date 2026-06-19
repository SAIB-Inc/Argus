using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;

namespace Argus.Sync.Example.Reducers;

/// <summary>
/// Second fork dependent of <see cref="LovelaceBalanceByAddressReducer"/>, sibling to
/// <see cref="WatchedAddressBalanceReducer"/>. Runs the same snapshot logic under its own name, so
/// the pair forms a 1-parent → 2-dependent fork: it proves the parent fans blocks out to both
/// children, that each child independently reads the parent's committed data with its own fresh
/// unit-of-work, and that rollbacks cascade to both.
/// </summary>
[DependsOn(typeof(LovelaceBalanceByAddressReducer))]
public class WatchedAddressBalanceSiblingReducer(IConfiguration configuration) : IReducer
{
    private readonly IReadOnlyList<(string Name, string Bech32)> _watched = WatchedAddressBalanceReducer.ReadWatched(configuration);

    /// <inheritdoc />
    public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(block);
        ulong slot = block.Header().HeaderBody().Slot();
        return WatchedAddressBalanceReducer.WriteSnapshotAsync(uow, nameof(WatchedAddressBalanceSiblingReducer), _watched, slot, ct);
    }

    /// <inheritdoc />
    public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
        => WatchedAddressBalanceReducer.RemoveSnapshotsAsync(uow, nameof(WatchedAddressBalanceSiblingReducer), slot, ct);
}
