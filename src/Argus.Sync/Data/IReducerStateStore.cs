using Argus.Sync.Data.Models;

namespace Argus.Sync.Data;

/// <summary>
/// Backend-agnostic store for reducer state checkpoints. The Argus library only
/// needs four operations on its own state, all keyed by reducer name. This
/// interface lets consumers swap between EF Core, plain SQL, MongoDB, RocksDB,
/// in-memory, etc. without touching reducer code.
/// </summary>
public interface IReducerStateStore
{
    /// <summary>
    /// Loads a single reducer's state by name, or null if no state has been
    /// persisted for that reducer yet.
    /// </summary>
    /// <param name="reducerName">The reducer's identifier (typically its type name without generic arity).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ReducerState?> GetAsync(string reducerName, CancellationToken ct = default);

    /// <summary>
    /// Loads multiple reducer states in one round-trip. Returned dictionary contains
    /// only the names that exist in the store; missing names are absent (not present
    /// with a null value).
    /// </summary>
    /// <param name="reducerNames">Names to load.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyDictionary<string, ReducerState>> GetManyAsync(
        IEnumerable<string> reducerNames,
        CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a single reducer's state, keyed by <see cref="ReducerState.Name"/>.
    /// Implementations must be idempotent: repeated calls with the same state must
    /// converge on the same persisted row.
    /// </summary>
    /// <param name="state">The state to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertAsync(ReducerState state, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates multiple reducer states in one round-trip. Equivalent to
    /// calling <see cref="UpsertAsync(ReducerState, CancellationToken)"/> in a loop
    /// but with the implementation free to batch.
    /// </summary>
    /// <param name="states">The states to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertManyAsync(IEnumerable<ReducerState> states, CancellationToken ct = default);
}
