using Argus.Sync.Reducers;
using Argus.Sync.Utils;

namespace Argus.Sync.Workers;

public partial class CardanoIndexWorker
{
    private void BuildDependencyGraph()
    {
        // Build reducer lookup and initialize dependency structures
        foreach (IReducer reducer in reducers)
        {
            string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            _reducersByName[reducerName] = reducer;
            _dependentReducers[reducerName] = [];
            _reducerDependency[reducerName] = null;
        }

        // Build dependency mappings
        foreach (IReducer reducer in reducers)
        {
            string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            Type? dependency = ReducerDependencyResolver.GetReducerDependency(reducer.GetType());

            if (dependency != null)
            {
                string dependencyName = ArgusUtil.GetTypeNameWithoutGenerics(dependency);

                // Validate dependency exists
                if (!_reducersByName.ContainsKey(dependencyName))
                {
                    throw new InvalidOperationException($"Reducer {reducerName} depends on {dependencyName}, but {dependencyName} is not registered.");
                }

                // Add to dependency mappings (single dependency only)
                _reducerDependency[reducerName] = dependencyName;
                _dependentReducers[dependencyName].Add(reducerName);
            }
        }

        // Identify root reducers (those with no dependencies)
        foreach (string reducerName in _reducersByName.Keys)
        {
            if (_reducerDependency[reducerName] == null)
            {
                _ = _rootReducers.Add(reducerName);
            }
        }

        LogDependencyGraphBuilt(logger, _rootReducers.Count, _reducersByName.Count);
    }

    /// <summary>
    /// Constructs one <see cref="ReducerGraphProcessor"/> per root reducer, each driving
    /// that root's entire reachable subgraph in topological order over a single batched
    /// unit of work. Must be called after <see cref="BuildDependencyGraph"/>.
    /// </summary>
    private void BuildGraphProcessors()
    {
        foreach (string rootName in _rootReducers)
        {
            _graphProcessors[rootName] = new ReducerGraphProcessor(
                topologicallyOrderedReducers: TopologicalOrderFromRoot(rootName),
                uowFactory: unitOfWorkFactory,
                channelCapacity: _pipelineChannelCapacity,
                batchSize: _commitBatchSize,
                maxBatchDelay: _commitMaxDelay,
                logger: logger,
                telemetryRecorder: RecordTelemetry,
                intersectionRecorder: UpdateInMemoryStateRollforward,
                rollbackRecorder: UpdateInMemoryStateRollback);
        }
    }

    /// <summary>
    /// Breadth-first order of a root's reachable subgraph. Every reducer has a single
    /// dependency (its parent), so breadth order is a valid topological order — a parent
    /// is always emitted before its children, which is what lets a child read its
    /// parent's same-block writes through the shared unit of work.
    /// </summary>
    private List<IReducer> TopologicalOrderFromRoot(string rootName)
    {
        List<IReducer> ordered = [];
        Queue<string> queue = new();
        queue.Enqueue(rootName);
        while (queue.Count > 0)
        {
            string name = queue.Dequeue();
            ordered.Add(_reducersByName[name]);
            foreach (string dependentName in _dependentReducers[name])
            {
                queue.Enqueue(dependentName);
            }
        }
        return ordered;
    }

    private void CollectAllDependentsRecursively(string reducerName, HashSet<string> collected)
    {
        if (_dependentReducers.TryGetValue(reducerName, out List<string>? dependents))
        {
            foreach (string dependent in dependents)
            {
                if (collected.Add(dependent))
                {
                    // Recursively collect all dependents of this dependent
                    CollectAllDependentsRecursively(dependent, collected);
                }
            }
        }
    }

    private string? GetRootReducerName(string reducerName)
    {
        // Follow dependency chain to find root reducer
        string current = reducerName;
        while (_reducerDependency.TryGetValue(current, out string? dependency) && dependency != null)
        {
            current = dependency;
        }
        return _rootReducers.Contains(current) ? current : null;
    }
}
