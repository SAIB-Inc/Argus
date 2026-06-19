using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using MongoDB.Driver;

namespace Argus.Sync.Tests.Mongo;

/// <summary>
/// MongoDB implementation of <see cref="IBlockUnitOfWork"/>. Wraps one client session with an open
/// multi-document transaction for the lifetime of a block-branch: reducers write through
/// <see cref="GetStorage{T}"/> (a <see cref="MongoStorage"/> carrying the session), and
/// <see cref="CommitAsync"/> applies the tracked checkpoints to the <c>ReducerStates</c> collection and
/// commits — data and checkpoint atomic in one transaction, exactly like the EF backend.
/// </summary>
/// <remarks>
/// Mongo has no change-tracker, so "did the reducer write data?" cannot be auto-detected — a reducer
/// that writes MUST call <see cref="MarkDataChanged"/> (every Mongo write is "non-tracked", the case
/// the EF backend documents). Without it, an empty-block commit is deferred and the writes are lost.
/// </remarks>
public sealed class MongoBlockUnitOfWork : IBlockUnitOfWork
{
    private readonly IClientSessionHandle _session;
    private readonly MongoStorage _storage;
    private readonly IMongoCollection<MongoReducerStateDoc> _states;
    private readonly int _rollbackBuffer;
    private readonly Dictionary<string, Point> _trackedIntersections = [];
    private readonly Dictionary<string, ulong> _trackedRollbacks = [];
    private bool _dataChanged;
    private bool _completed;
    private bool _disposed;

    /// <summary>Wraps a session whose transaction has already been started by the factory.</summary>
    public MongoBlockUnitOfWork(IMongoDatabase database, IClientSessionHandle session, int rollbackBuffer)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _storage = new MongoStorage(database, session);
        _states = database.GetCollection<MongoReducerStateDoc>("ReducerStates");
        _rollbackBuffer = Math.Max(1, rollbackBuffer);
    }

    /// <inheritdoc />
    public T GetStorage<T>() where T : class
    {
        if (_storage is not T typed)
        {
            throw new InvalidCastException($"The Mongo unit of work exposes {nameof(MongoStorage)}, not {typeof(T).Name}.");
        }
        return typed;
    }

    /// <inheritdoc />
    public void TrackIntersection(string reducerName, Point point)
    {
        ArgumentNullException.ThrowIfNull(reducerName);
        ArgumentNullException.ThrowIfNull(point);
        _trackedIntersections[reducerName] = point;
        _ = _trackedRollbacks.Remove(reducerName);
    }

    /// <inheritdoc />
    public void TrackRollback(string reducerName, ulong rollbackSlot)
    {
        ArgumentNullException.ThrowIfNull(reducerName);
        _ = _trackedIntersections.Remove(reducerName);
        _trackedRollbacks[reducerName] = rollbackSlot;
    }

    /// <inheritdoc />
    public void MarkDataChanged() => _dataChanged = true;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, Point> TrackedIntersections => _trackedIntersections;

    /// <inheritdoc />
    public async Task<bool> CommitAsync(bool deferIfEmpty = false, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // No change-tracker on Mongo: data changes are known only via MarkDataChanged.
        bool hasDataChanges = _dataChanged;
        bool hasRollbackStateChanges = _trackedRollbacks.Count > 0;
        if (deferIfEmpty && !hasDataChanges && !hasRollbackStateChanges)
        {
            await _session.AbortTransactionAsync(ct).ConfigureAwait(false);
            _completed = true;
            _dataChanged = false;
            return false;
        }

        await ApplyTrackedReducerStatesAsync(ct).ConfigureAwait(false);
        await _session.CommitTransactionAsync(ct).ConfigureAwait(false);
        _completed = true;
        ClearAfterCompletion();
        return true;
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            return;
        }
        if (!_completed)
        {
            await _session.AbortTransactionAsync(ct).ConfigureAwait(false);
            _completed = true;
        }
        ClearAfterCompletion();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (!_completed)
        {
            try { await _session.AbortTransactionAsync().ConfigureAwait(false); }
            catch { /* dispose must not mask the original pipeline error */ }
            _completed = true;
        }

        _session.Dispose();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    private void ClearAfterCompletion()
    {
        _trackedIntersections.Clear();
        _trackedRollbacks.Clear();
        _dataChanged = false;
    }

    private async Task ApplyTrackedReducerStatesAsync(CancellationToken ct)
    {
        List<string> names = [.. _trackedIntersections.Keys.Concat(_trackedRollbacks.Keys).Distinct()];
        if (names.Count == 0)
        {
            return;
        }

        FilterDefinition<MongoReducerStateDoc> inNames = Builders<MongoReducerStateDoc>.Filter.In(x => x.Name, names);
        List<MongoReducerStateDoc> existing = await _states.Find(_session, inNames).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, ReducerState> byName = existing.ToDictionary(d => d.Name, d => d.ToReducerState());

        foreach ((string reducerName, ulong rollbackSlot) in _trackedRollbacks)
        {
            if (byName.TryGetValue(reducerName, out ReducerState? row))
            {
                row.LatestIntersections = ReducerStateCheckpointWindow.ApplyRollback(row.LatestIntersections, rollbackSlot, _rollbackBuffer);
                await ReplaceAsync(reducerName, row, ct).ConfigureAwait(false);
            }
        }

        foreach ((string reducerName, Point point) in _trackedIntersections)
        {
            if (byName.TryGetValue(reducerName, out ReducerState? row))
            {
                row.LatestIntersections = ReducerStateCheckpointWindow.AddRollForward(row.LatestIntersections, point, _rollbackBuffer);
                await ReplaceAsync(reducerName, row, ct).ConfigureAwait(false);
            }
            else
            {
                ReducerState fresh = new(reducerName, DateTimeOffset.UtcNow)
                {
                    StartIntersection = point,
                    LatestIntersections = [point],
                };
                await _states.InsertOneAsync(_session, MongoReducerStateDoc.FromReducerState(fresh), cancellationToken: ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ReplaceAsync(string reducerName, ReducerState row, CancellationToken ct)
    {
        FilterDefinition<MongoReducerStateDoc> byId = Builders<MongoReducerStateDoc>.Filter.Eq(x => x.Name, reducerName);
        _ = await _states.ReplaceOneAsync(_session, byId, MongoReducerStateDoc.FromReducerState(row), cancellationToken: ct).ConfigureAwait(false);
    }
}
