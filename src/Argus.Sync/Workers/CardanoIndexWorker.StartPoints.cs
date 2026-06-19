using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Workers;

public partial class CardanoIndexWorker
{
    /// <summary>
    /// Updates the in-memory <see cref="ReducerState"/> with a new intersection
    /// point. Called by <see cref="ReducerPipeline"/> after each successful
    /// roll-forward, before the branch UoW commits. Pure in-memory; the
    /// authoritative checkpoint write happens via the UoW.
    /// </summary>
    private void UpdateInMemoryStateRollforward(string reducerName, Point point)
    {
        if (_reducerStates.TryGetValue(reducerName, out ReducerState? currentState))
        {
            _reducerStates[reducerName] = currentState with
            {
                LatestIntersections = ReducerStateCheckpointWindow.AddRollForward(
                    currentState.LatestIntersections,
                    point,
                    _rollbackBuffer)
            };
        }
    }

    private void UpdateInMemoryStateRollback(string reducerName, ulong rollbackSlot)
    {
        if (_reducerStates.TryGetValue(reducerName, out ReducerState? currentState))
        {
            _reducerStates[reducerName] = currentState with
            {
                LatestIntersections = ReducerStateCheckpointWindow.ApplyRollback(
                    currentState.LatestIntersections,
                    rollbackSlot,
                    _rollbackBuffer)
            };
        }
    }

    private async Task InitializeAllReducerStatesAsync(CancellationToken stoppingToken)
    {
        // First, load all reducer states
        foreach ((string? reducerName, IReducer _) in _reducersByName)
        {
            ReducerState state = await GetReducerStateAsync(reducerName, stoppingToken);
            _reducerStates[reducerName] = state;
        }

        // Then adjust dependent start points in topological order
        HashSet<string> processedDependents = [];
        await AdjustAllDependentStartPointsAsync(processedDependents, stoppingToken);
    }

    private async Task AdjustAllDependentStartPointsAsync(HashSet<string> processedDependents, CancellationToken stoppingToken)
    {
        // Process all dependents in topological order (dependencies first)
        List<string> dependentsToProcess = [.. _reducersByName.Keys
            .Where(r => !_rootReducers.Contains(r) && !processedDependents.Contains(r))];

        foreach (string? dependentName in dependentsToProcess)
        {
            await AdjustDependentStartPointRecursivelyAsync(dependentName, processedDependents, stoppingToken);
        }
    }

    private async Task AdjustDependentStartPointRecursivelyAsync(
        string dependentName,
        HashSet<string> processedDependents,
        CancellationToken stoppingToken)
    {
        if (processedDependents.Contains(dependentName))
        {
            return;
        }

        string? dependency = _reducerDependency[dependentName];
        if (dependency == null)
        {
            _ = processedDependents.Add(dependentName);
            return;
        }

        // First ensure the dependency is processed (recursive call)
        if (!_rootReducers.Contains(dependency) && !processedDependents.Contains(dependency))
        {
            await AdjustDependentStartPointRecursivelyAsync(dependency, processedDependents, stoppingToken);
        }

        // Now adjust this dependent's start point
        AdjustDependentStartPoint(dependentName);
        _ = processedDependents.Add(dependentName);
    }

    private void AdjustDependentStartPoint(string dependentName)
    {
        string? dependency = _reducerDependency[dependentName];
        if (dependency == null)
        {
            return;
        }

        if (!_reducerStates.TryGetValue(dependency, out ReducerState? depState))
        {
            LogDependencyStateNotFound(logger, dependency, dependentName);
            return;
        }

        ReducerState dependentState = _reducerStates[dependentName];

        // Handle bootstrap case - if dependency hasn't processed any blocks yet
        if (!depState.LatestIntersections.Any())
        {
            // If dependent also hasn't started, they can share the same default start point
            if (!dependentState.LatestIntersections.Any())
            {
                LogBothAtInitialState(logger, dependentName, dependency);
                return;
            }

            // If dependent has already processed blocks but dependency hasn't, this is an error state
            LogDependentAheadOfDependency(logger, dependentName, dependency);
            return;
        }

        // Get the latest intersection point from dependency (with proper hash)
        Point? dependencyLatestPoint = depState.LatestIntersections
            .OrderByDescending(p => p.Slot)
            .FirstOrDefault();

        if (dependencyLatestPoint == null || dependencyLatestPoint.Slot == 0)
        {
            LogInvalidDependencyIntersection(logger, dependency);
            return;
        }

        // Case 1: Dependent hasn't started yet or is behind dependency
        if (dependentState.StartIntersection.Slot < dependencyLatestPoint.Slot)
        {
            string hashDisplay = dependencyLatestPoint.Hash.Length > 8
                ? dependencyLatestPoint.Hash[..8] + "..."
                : dependencyLatestPoint.Hash;
            LogAdjustingStartPoint(logger, dependentName,
                dependentState.StartIntersection.Slot,
                dependencyLatestPoint.Slot,
                hashDisplay,
                dependency);

            // Use the actual intersection point from dependency (with proper hash)
            _reducerStates[dependentName] = dependentState with
            {
                StartIntersection = dependencyLatestPoint
            };
        }
        // Case 2: Dependent is ahead of dependency
        else if (dependentState.StartIntersection.Slot > dependencyLatestPoint.Slot)
        {
            // Check if dependent has already processed blocks
            if (dependentState.LatestIntersections.Any())
            {
                ulong dependentLatestSlot = dependentState.LatestSlot;
                if (dependentLatestSlot > dependencyLatestPoint.Slot)
                {
                    LogInconsistentState(logger, dependentName, dependentLatestSlot, dependency, dependencyLatestPoint.Slot);
                }
            }
            else
            {
                LogWaitingForDependency(logger, dependentName, dependentState.StartIntersection.Slot, dependency, dependencyLatestPoint.Slot);
            }
        }
        // Case 3: They're at the same slot
        else
        {
            LogSynchronizedAtSlot(logger, dependentName, dependency, dependencyLatestPoint.Slot);
        }
    }

    private async Task<ReducerState> GetReducerStateAsync(string reducerName, CancellationToken stoppingToken)
    {
        ReducerState? state = await unitOfWorkFactory.GetReducerStateAsync(reducerName, stoppingToken).ConfigureAwait(false);

        if (state is not null)
        {
            IEnumerable<Point> intersections = state.LatestIntersections.Any()
                ? state.LatestIntersections
                : [state.StartIntersection];

            return state with
            {
                LatestIntersections = ReducerStateCheckpointWindow.Normalize(intersections, _rollbackBuffer)
            };
        }

        return GetDefaultReducerState(reducerName);
    }

    private ReducerState GetDefaultReducerState(string reducerName)
    {
        IConfigurationSection reducerSection = configuration.GetSection($"CardanoIndexReducers:{reducerName}");
        ulong configStartSlot = reducerSection.GetValue<ulong?>("StartSlot") ?? _defaultStartSlot;
        string configStartHash = reducerSection.GetValue<string>("StartHash") ?? _defaultStartHash;
        Point defaultIntersection = new(configStartHash, configStartSlot);
        List<Point> latestIntersections = [defaultIntersection];
        ReducerState initialState = new(reducerName, DateTimeOffset.UtcNow)
        {
            StartIntersection = defaultIntersection,
            LatestIntersections = latestIntersections
        };

        return initialState;
    }

    private IEnumerable<Point> GetSafeIntersectionPoints(string reducerName)
    {
        // For root reducers with dependents, find the oldest intersection point
        // across all reducers in the dependency chain to ensure safe rollback
        if (_rootReducers.Contains(reducerName) && _dependentReducers.TryGetValue(reducerName, out List<string>? dependents) && dependents.Count > 0)
        {
            HashSet<string> allReducersInChain = [reducerName];
            CollectAllDependentsRecursively(reducerName, allReducersInChain);

            // Find the OLDEST (minimum slot) intersection across all reducers in the chain
            Point? oldestIntersection = null;
            ulong oldestSlot = ulong.MaxValue;

            foreach (string chainReducerName in allReducersInChain)
            {
                if (_reducerStates.TryGetValue(chainReducerName, out ReducerState? state) && state.LatestIntersections.Any())
                {
                    // Get the latest intersection for this reducer
                    Point? latestIntersection = state.LatestIntersections
                        .OrderByDescending(p => p.Slot)
                        .FirstOrDefault();

                    if (latestIntersection != null && latestIntersection.Slot < oldestSlot)
                    {
                        oldestSlot = latestIntersection.Slot;
                        oldestIntersection = latestIntersection;
                    }
                }
            }

            if (oldestIntersection != null)
            {
                // Return all intersection points from root reducer that are <= oldest dependent's slot
                // This preserves the rollback buffer while still respecting dependent reducer progress
                List<Point> rootIntersections = [.. _reducerStates[reducerName].LatestIntersections
                    .Where(p => p.Slot <= oldestSlot)
                    .OrderByDescending(p => p.Slot)];

                if (rootIntersections.Count > 0)
                {
                    LogRootReducerIntersections(logger, reducerName, rootIntersections.Count, oldestSlot, allReducersInChain.Count);
                    return rootIntersections;
                }

                // Fallback to the oldest intersection if no root intersections match
                LogRootReducerFallback(logger, reducerName, oldestSlot);
                return [oldestIntersection];
            }
        }

        // For reducers without dependents or dependent reducers themselves, use their own intersections
        return _reducerStates[reducerName].LatestIntersections;
    }
}
