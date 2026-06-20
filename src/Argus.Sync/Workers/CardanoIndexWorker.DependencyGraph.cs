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
    /// Constructs one <see cref="ReducerPipeline"/> per reducer and wires the
    /// dependency-graph topology into bounded-channel edges. Must be called
    /// after <see cref="BuildDependencyGraph"/>.
    /// </summary>
    private void BuildPipelines()
    {
        foreach ((string reducerName, IReducer reducer) in _reducersByName)
        {
            // Batch-commit only standalone reducers — no dependency AND no dependents,
            // so the pipeline owns its unit of work outright. Chain members and fork
            // children forward or receive a shared UoW and must commit per block, so
            // they run with batchSize 1.
            bool standalone = _reducerDependency[reducerName] is null
                && _dependentReducers[reducerName].Count == 0;
            _pipelines[reducerName] = new ReducerPipeline(
                reducer: reducer,
                uowFactory: unitOfWorkFactory,
                channelCapacity: _pipelineChannelCapacity,
                logger: logger,
                telemetryRecorder: RecordTelemetry,
                intersectionRecorder: UpdateInMemoryStateRollforward,
                rollbackRecorder: UpdateInMemoryStateRollback,
                batchSize: standalone ? _commitBatchSize : 1,
                maxBatchDelay: _commitMaxDelay);
        }

        foreach ((string parentName, List<string> dependentNames) in _dependentReducers)
        {
            ReducerPipeline parent = _pipelines[parentName];
            foreach (string depName in dependentNames)
            {
                parent.AddDependent(_pipelines[depName]);
            }
        }

        // Each root pipeline's chain consumer (StartReducerChainSyncAsync) is
        // the one upstream producer — register that vote so completion fires
        // after the chain consumer signals done.
        foreach (string rootName in _rootReducers)
        {
            _pipelines[rootName].IncrementExpectedVotes();
        }
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
