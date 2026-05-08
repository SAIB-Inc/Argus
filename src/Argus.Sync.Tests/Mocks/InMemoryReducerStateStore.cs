using System.Collections.Concurrent;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;

namespace Argus.Sync.Tests.Mocks;

/// <summary>
/// In-memory implementation of <see cref="IReducerStateStore"/> for tests.
/// Avoids the Postgres dependency for tests that only exercise the worker's
/// state-management logic, not its persistence shape.
/// </summary>
public sealed class InMemoryReducerStateStore : IReducerStateStore
{
    private readonly ConcurrentDictionary<string, ReducerState> _states = new();

    public Task<ReducerState?> GetAsync(string reducerName, CancellationToken ct = default)
    {
        _ = _states.TryGetValue(reducerName, out ReducerState? state);
        return Task.FromResult(state);
    }

    public Task<IReadOnlyDictionary<string, ReducerState>> GetManyAsync(
        IEnumerable<string> reducerNames,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reducerNames);

        Dictionary<string, ReducerState> result = [];
        foreach (string name in reducerNames)
        {
            if (_states.TryGetValue(name, out ReducerState? state))
            {
                result[name] = state;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, ReducerState>>(result);
    }

    public Task UpsertAsync(ReducerState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _states[state.Name] = state;
        return Task.CompletedTask;
    }

    public Task UpsertManyAsync(IEnumerable<ReducerState> states, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(states);
        foreach (ReducerState state in states)
        {
            _states[state.Name] = state;
        }
        return Task.CompletedTask;
    }
}
