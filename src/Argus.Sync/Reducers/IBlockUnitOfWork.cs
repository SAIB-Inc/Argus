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
/// the underlying connection participates in the framework-owned transaction.
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
    /// Persists all registered changes (reducer data + tracked checkpoints) in
    /// a single transaction. Called by the framework once per block per
    /// dependency-graph branch.
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Discards all registered changes. Called by the framework on
    /// per-block error to prevent partial data from landing.
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);
}
