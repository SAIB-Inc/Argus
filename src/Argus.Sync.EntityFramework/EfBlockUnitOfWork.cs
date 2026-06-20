using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Argus.Sync.EntityFramework;

/// <summary>
/// EF Core implementation of <see cref="IBlockUnitOfWork"/>. Wraps a single
/// <see cref="DbContext"/> instance for the lifetime of one block-branch:
/// every reducer in the branch gets the same context via <see cref="GetStorage{T}"/>,
/// pending writes are visible across reducers via the change-tracker, and
/// <see cref="CommitAsync"/> persists data + tracked checkpoints in one
/// transaction. After commit (success or failure), the change-tracker is
/// cleared to keep memory bounded for long-running indexers.
/// </summary>
public sealed class EfBlockUnitOfWork<TContext> : IBlockUnitOfWork
    where TContext : CardanoDbContext
{
    private readonly TContext _dbContext;
    private readonly IDbContextTransaction _transaction;
    private readonly int _rollbackBuffer;
    private readonly Dictionary<string, Point> _trackedIntersections = [];
    private readonly Dictionary<string, ulong> _trackedRollbacks = [];
    private bool _dataChanged;
    private bool _completed;
    private bool _disposed;

    /// <summary>
    /// Creates a UoW that owns the given context and opens a transaction
    /// synchronously. Prefer <see cref="EfBlockUnitOfWorkFactory{TContext}"/>
    /// in framework code so transaction creation can be async.
    /// </summary>
    public EfBlockUnitOfWork(TContext dbContext)
        : this(dbContext, BeginTransaction(dbContext), ReducerStateCheckpointWindow.DefaultMaxCount)
    {
    }

    /// <summary>
    /// Creates a UoW that owns the given context and transaction. The
    /// transaction is committed by <see cref="CommitAsync"/> or rolled back by
    /// <see cref="RollbackAsync"/>/<see cref="DisposeAsync"/>.
    /// </summary>
    public EfBlockUnitOfWork(TContext dbContext, IDbContextTransaction transaction, int rollbackBuffer = ReducerStateCheckpointWindow.DefaultMaxCount)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(transaction);
        _dbContext = dbContext;
        _transaction = transaction;
        _rollbackBuffer = Math.Max(1, rollbackBuffer);
        // Batch-commit calls SaveChanges once per block within this single
        // transaction; EF's default per-SaveChanges savepoints are unnecessary (the
        // whole batch is the atomic unit — a fault discards it via RollbackAsync) and
        // trip Npgsql under repeated same-transaction SaveChanges. Disable them.
        _dbContext.Database.AutoSavepointsEnabled = false;
    }

    /// <inheritdoc />
    public T GetStorage<T>() where T : class
    {
        if (_dbContext is not T typed)
        {
            throw new InvalidCastException(
                $"Cannot cast underlying DbContext ({typeof(TContext).Name}) to {typeof(T).Name}.");
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
        ThrowIfDisposed();

        // Snapshot data-change state before we add state-row writes; otherwise
        // tracked ReducerState entities would inflate the count and fool the
        // deferral check. Rollback checkpoint rewinds are never deferred.
        bool hasDataChanges = _dataChanged || _dbContext.ChangeTracker.HasChanges();
        bool hasRollbackStateChanges = _trackedRollbacks.Count > 0;
        if (deferIfEmpty && !hasDataChanges && !hasRollbackStateChanges)
        {
            // No reducer wrote anything for this block; caller preserves
            // _trackedIntersections via TrackedIntersections and re-tracks them
            // against the next block. We still clear the tracker (cheap) but
            // leave _trackedIntersections intact so the caller can read them.
            _dbContext.ChangeTracker.Clear();
            _dataChanged = false;
            return false;
        }

        await ApplyTrackedReducerStatesAsync(ct).ConfigureAwait(false);
        _ = await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        await _transaction.CommitAsync(ct).ConfigureAwait(false);
        _completed = true;

        ClearAfterCompletion();
        return true;
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        // Push this block's writes into the open transaction (no commit, no fsync)
        // so later blocks in the same batch can read them via a query within the
        // transaction, then clear the change-tracker to keep EF's O(n^2) tracker
        // bounded across the batch — the rows live in the transaction, so a
        // subsequent query re-fetches and re-tracks them. Checkpoints are NOT
        // written here; they ride the batch's single CommitAsync.
        int written = await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        if (written > 0)
        {
            _dataChanged = true;
        }
        _dbContext.ChangeTracker.Clear();
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
            await _transaction.RollbackAsync(ct).ConfigureAwait(false);
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
            try
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch
            {
                // Dispose should not mask the original pipeline error.
            }
            _completed = true;
        }

        await _transaction.DisposeAsync().ConfigureAwait(false);
        await _dbContext.DisposeAsync().ConfigureAwait(false);
    }

    private void ClearAfterCompletion()
    {
        // Long-running indexers must clear the change-tracker after each
        // commit — EF's tracker is O(n^2) past a few thousand entities.
        _dbContext.ChangeTracker.Clear();
        _trackedIntersections.Clear();
        _trackedRollbacks.Clear();
        _dataChanged = false;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static IDbContextTransaction BeginTransaction(TContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return dbContext.Database.BeginTransaction();
    }

    private async Task ApplyTrackedReducerStatesAsync(CancellationToken ct)
    {
        List<string> names = [.. _trackedIntersections.Keys.Concat(_trackedRollbacks.Keys).Distinct()];
        if (names.Count == 0)
        {
            return;
        }

        List<ReducerState> existing = await _dbContext.ReducerStates
            .Where(r => names.Contains(r.Name))
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, ReducerState> existingByName = existing.ToDictionary(r => r.Name);

        foreach ((string reducerName, ulong rollbackSlot) in _trackedRollbacks)
        {
            if (existingByName.TryGetValue(reducerName, out ReducerState? row))
            {
                row.LatestIntersections = ReducerStateCheckpointWindow.ApplyRollback(
                    row.LatestIntersections,
                    rollbackSlot,
                    _rollbackBuffer);
            }
        }

        foreach ((string reducerName, Point point) in _trackedIntersections)
        {
            if (existingByName.TryGetValue(reducerName, out ReducerState? row))
            {
                row.LatestIntersections = ReducerStateCheckpointWindow.AddRollForward(
                    row.LatestIntersections,
                    point,
                    _rollbackBuffer);
            }
            else
            {
                ReducerState fresh = new(reducerName, DateTimeOffset.UtcNow)
                {
                    StartIntersection = point,
                    LatestIntersections = [point],
                };
                _ = _dbContext.ReducerStates.Add(fresh);
            }
        }
    }
}
