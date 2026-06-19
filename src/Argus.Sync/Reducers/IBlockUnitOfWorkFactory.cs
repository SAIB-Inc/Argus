using Argus.Sync.Data.Models;

namespace Argus.Sync.Reducers;

/// <summary>
/// The storage-backend seam: produces per-block, per-branch <see cref="IBlockUnitOfWork"/> instances
/// and reads persisted reducer checkpoints. A non-EF backend (e.g. MongoDB) implements this one
/// interface — the reducer's own data access goes through <see cref="IBlockUnitOfWork.GetStorage{T}"/>.
/// </summary>
public interface IBlockUnitOfWorkFactory
{
    /// <summary>
    /// Creates a fresh unit of work for one block-branch. The framework calls this at the start of
    /// each branch's processing for a block and disposes the UoW (commit or rollback) when it completes.
    /// </summary>
    Task<IBlockUnitOfWork> CreateAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads a reducer's persisted checkpoint by name, or null if none exists yet. Read once per
    /// reducer at startup so the worker can resume from where it left off. Checkpoints are written
    /// transactionally with reducer data via <see cref="IBlockUnitOfWork"/>, never separately.
    /// </summary>
    /// <param name="reducerName">The reducer's identifier (type name without generic arity).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ReducerState?> GetReducerStateAsync(string reducerName, CancellationToken ct = default);
}
