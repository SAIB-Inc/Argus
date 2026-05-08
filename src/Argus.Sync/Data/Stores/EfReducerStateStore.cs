using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Data.Stores;

/// <summary>
/// EF Core implementation of <see cref="IReducerStateStore"/>. The default store
/// registered by <c>AddCardanoIndexer</c>; consumers using a non-relational backend
/// register their own implementation instead.
/// </summary>
/// <typeparam name="TContext">The consumer's <see cref="CardanoDbContext"/>-derived context type.</typeparam>
public sealed class EfReducerStateStore<TContext>(IDbContextFactory<TContext> dbContextFactory)
    : IReducerStateStore
    where TContext : CardanoDbContext
{
    /// <inheritdoc />
    public async Task<ReducerState?> GetAsync(string reducerName, CancellationToken ct = default)
    {
        await using TContext dbContext = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ReducerState? state = await dbContext.ReducerStates
            .AsNoTracking()
            .Where(s => s.Name == reducerName)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return state;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ReducerState>> GetManyAsync(
        IEnumerable<string> reducerNames,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reducerNames);
        List<string> names = [.. reducerNames];
        if (names.Count == 0)
        {
            return new Dictionary<string, ReducerState>();
        }

        await using TContext dbContext = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        List<ReducerState> rows = await dbContext.ReducerStates
            .AsNoTracking()
            .Where(s => names.Contains(s.Name))
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.ToDictionary(r => r.Name);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ReducerState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await using TContext dbContext = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        ReducerState? existing = await dbContext.ReducerStates
            .FirstOrDefaultAsync(r => r.Name == state.Name, ct).ConfigureAwait(false);

        if (existing is null)
        {
            _ = dbContext.ReducerStates.Add(state);
        }
        else
        {
            existing.StartIntersectionJson = state.StartIntersectionJson;
            existing.LatestIntersectionsJson = state.LatestIntersectionsJson;
        }

        _ = await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertManyAsync(IEnumerable<ReducerState> states, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(states);
        List<ReducerState> incoming = [.. states];
        if (incoming.Count == 0)
        {
            return;
        }

        await using TContext dbContext = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        List<string> names = [.. incoming.Select(s => s.Name)];
        List<ReducerState> existing = await dbContext.ReducerStates
            .Where(r => names.Contains(r.Name))
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<string, ReducerState> existingByName = existing.ToDictionary(r => r.Name);

        foreach (ReducerState newState in incoming)
        {
            if (existingByName.TryGetValue(newState.Name, out ReducerState? row))
            {
                row.StartIntersectionJson = newState.StartIntersectionJson;
                row.LatestIntersectionsJson = newState.LatestIntersectionsJson;
            }
            else
            {
                _ = dbContext.ReducerStates.Add(newState);
            }
        }

        _ = await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
