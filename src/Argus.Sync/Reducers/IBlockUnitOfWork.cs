using Argus.Sync.Data.Models;

namespace Argus.Sync.Reducers;

/// <summary>
/// Framework-managed unit of work passed to a reducer for the duration of a single
/// block's processing within one dependency-graph branch. The reducer registers its
/// data writes against the underlying storage handle (<see cref="GetStorage{T}"/>); the
/// framework commits once per branch via <see cref="CommitAsync"/>, atomically
/// persisting reducer data alongside the tracked checkpoints.
///
/// Reducers do **not** call SaveChangesAsync (or backend equivalent) themselves —
/// the framework owns commit timing. Reducers retain raw access to the underlying
/// storage handle for arbitrary operations: tracked entities, ExecuteUpdate /
/// ExecuteDelete, raw SQL via Database.ExecuteSqlRawAsync, ADO.NET via
/// Database.GetDbConnection, third-party bulk extensions, etc. Anything joined to
/// the underlying connection/transaction participates in the framework-owned
/// transaction. Non-tracked writes must call <see cref="MarkDataChanged"/> so
/// empty-block commit deferral does not skip their checkpoint write.
/// </summary>
public interface IBlockUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Type-safe accessor for the underlying storage. Returns the same instance
    /// for every reducer in the same branch — pending writes from upstream
    /// reducers are visible to downstream reducers via the storage's local
    /// change-tracker (e.g. <c>ctx.MyTable.Local.FirstOrDefault(...)</c>).
    /// Named <c>GetStorage</c> rather than <c>As</c> because <c>As</c> is a
    /// reserved keyword in some .NET languages (CA1716).
    /// </summary>
    /// <typeparam name="T">The expected backend type (e.g. consumer's DbContext).</typeparam>
    /// <exception cref="InvalidCastException">If the backend cannot be cast to T.</exception>
    T GetStorage<T>() where T : class;

    /// <summary>
    /// Records that <paramref name="reducerName"/> has processed a block at
    /// <paramref name="point"/>. The framework persists this checkpoint
    /// atomically with the reducer's data writes when <see cref="CommitAsync"/>
    /// runs. Called by the framework after each reducer in the branch processes
    /// a block — reducers do not call this directly.
    /// </summary>
    void TrackIntersection(string reducerName, Point point);

    /// <summary>
    /// Records that <paramref name="reducerName"/> rolled back to
    /// <paramref name="rollbackSlot"/>. The framework persists the checkpoint
    /// rewind atomically with the reducer's rollback data writes.
    /// </summary>
    void TrackRollback(string reducerName, ulong rollbackSlot);

    /// <summary>
    /// Marks that reducer data changed through a path the backend cannot
    /// auto-detect (for example raw SQL, ExecuteUpdate/ExecuteDelete, ADO.NET,
    /// or bulk extensions). Tracked EF entity changes are detected automatically.
    /// </summary>
    void MarkDataChanged();

    /// <summary>
    /// Read-only view of intersections registered via <see cref="TrackIntersection"/>
    /// since the last commit/rollback. Allows the framework to preserve state
    /// across deferred commits (see <see cref="CommitAsync"/>).
    /// </summary>
    IReadOnlyDictionary<string, Point> TrackedIntersections { get; }

    /// <summary>
    /// Persists all registered changes (reducer data + tracked checkpoints) in
    /// a single transaction. Called by the framework once per block per
    /// dependency-graph branch.
    /// </summary>
    /// <param name="deferIfEmpty">
    /// When true, skip the commit entirely if no reducer in the branch tracked
    /// any data changes for this block. Returns false to signal the caller
    /// should preserve <see cref="TrackedIntersections"/> and re-track them
    /// against the next block's UoW. Crash-safety is preserved: deferred
    /// intersections only cover blocks the reducer didn't write to, so replay
    /// after crash is a no-op for those blocks.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if a commit happened; false if deferred (only when <paramref name="deferIfEmpty"/> is true).</returns>
    Task<bool> CommitAsync(bool deferIfEmpty = false, CancellationToken ct = default);

    /// <summary>
    /// Discards all registered changes. Called by the framework on
    /// per-block error to prevent partial data from landing.
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);
}
