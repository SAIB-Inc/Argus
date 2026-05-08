using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Data.Stores;

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
    private readonly Dictionary<string, Point> _trackedIntersections = [];
    private bool _disposed;

    /// <summary>
    /// Creates a UoW that owns the given context. The context is disposed
    /// when this UoW is disposed.
    /// </summary>
    public EfBlockUnitOfWork(TContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
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
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_trackedIntersections.Count > 0)
        {
            await UpsertTrackedIntersectionsAsync(ct).ConfigureAwait(false);
        }

        _ = await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

        // Long-running indexers must clear the change-tracker after each
        // commit — EF's tracker is O(n^2) past a few thousand entities.
        _dbContext.ChangeTracker.Clear();
        _trackedIntersections.Clear();
    }

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken ct = default)
    {
        // Discard pending changes by clearing the tracker; nothing has been
        // saved yet because CommitAsync hasn't been called.
        _dbContext.ChangeTracker.Clear();
        _trackedIntersections.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        await _dbContext.DisposeAsync().ConfigureAwait(false);
    }

    private async Task UpsertTrackedIntersectionsAsync(CancellationToken ct)
    {
        List<string> names = [.. _trackedIntersections.Keys];
        List<ReducerState> existing = await _dbContext.ReducerStates
            .Where(r => names.Contains(r.Name))
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, ReducerState> existingByName = existing.ToDictionary(r => r.Name);

        foreach ((string reducerName, Point point) in _trackedIntersections)
        {
            if (existingByName.TryGetValue(reducerName, out ReducerState? row))
            {
                List<Point> updated = [point, .. row.LatestIntersections.Where(p => p.Slot < point.Slot)];
                row.LatestIntersections = updated;
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
