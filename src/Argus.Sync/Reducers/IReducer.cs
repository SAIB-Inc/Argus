using IBlock = Chrysalis.Codec.Types.Cardano.Core.IBlock;

namespace Argus.Sync.Reducers;

/// <summary>
/// Reducer that processes blocks and rollbacks for the chain-sync worker. The
/// framework provides an <see cref="IBlockUnitOfWork"/> per block-branch;
/// reducers register their data writes against it and the framework commits
/// atomically with the reducer's checkpoint.
///
/// Implementations should call <c>uow.As&lt;TBackend&gt;()</c> to get the
/// underlying storage handle and use it for any DB operation. Reducers must
/// **not** call <c>SaveChangesAsync</c> (or backend equivalent) — the framework
/// owns commit timing.
/// </summary>
public interface IReducer
{
    /// <summary>
    /// Processes a new block during chain synchronization. The framework calls
    /// <see cref="IBlockUnitOfWork.CommitAsync"/> once per branch after the last
    /// reducer in the branch returns; do not commit from within the reducer.
    /// </summary>
    /// <param name="block">The block to process.</param>
    /// <param name="uow">The branch's unit of work — register all DB writes here.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct);

    /// <summary>
    /// Handles a chain reorganization by rolling back to <paramref name="slot"/>.
    /// Same UoW semantics as <see cref="RollForwardAsync"/>: all writes go into
    /// the branch UoW; the framework commits.
    /// </summary>
    /// <param name="slot">The slot to roll back to (semantics depend on the chain provider's <c>RollBackType</c>).</param>
    /// <param name="uow">The branch's unit of work.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct);
}
