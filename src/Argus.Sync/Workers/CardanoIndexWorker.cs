using System.Collections.Concurrent;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using NextResponse = Argus.Sync.Data.Models.NextResponse;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using System.Diagnostics;

namespace Argus.Sync.Workers;

/// <summary>
/// Background service that manages Cardano blockchain synchronization by coordinating
/// multiple reducers, handling block processing, rollbacks, and chain provider connections.
/// </summary>
/// <typeparam name="T">The database context type, must inherit from <see cref="CardanoDbContext"/>.</typeparam>
/// <param name="configuration">Application configuration for sync settings.</param>
/// <param name="logger">Logger instance for diagnostic output.</param>
/// <param name="dbContextFactory">Factory for creating database context instances.</param>
/// <param name="reducers">Collection of registered reducer instances.</param>
/// <param name="chainProviderFactory">Factory for creating chain provider connections.</param>
public partial class CardanoIndexWorker<T>(
    IConfiguration configuration,
    ILogger<CardanoIndexWorker<T>> logger,
    IDbContextFactory<T> dbContextFactory,
    IEnumerable<IReducer<IReducerModel>> reducers,
    IChainProviderFactory chainProviderFactory
) : BackgroundService where T : CardanoDbContext
{
    private readonly ConcurrentDictionary<string, ReducerState> _reducerStates = [];
    private ulong EffectiveTipSlot;
    private readonly ConcurrentDictionary<string, List<long>> _processingTimes = [];
    private readonly ConcurrentDictionary<string, ulong> _latestSlots = [];

    // TUI update event mechanism
    private readonly SemaphoreSlim _tuiUpdateSignal = new(0, 1);
    private DateTime _lastTuiUpdate = DateTime.MinValue;

    // Dependency graph structures
    private readonly Dictionary<string, IReducer<IReducerModel>> _reducersByName = [];
    private readonly Dictionary<string, List<string>> _dependentReducers = []; // parent -> dependents
    private readonly Dictionary<string, string?> _reducerDependency = []; // reducer -> its single dependency
    private readonly HashSet<string> _rootReducers = [];
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastStartPointChecks = [];
    private readonly ConcurrentDictionary<string, ICardanoChainProvider> _rootReducerProviders = [];

    private readonly long _maxRollbackSlots = configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000);
    private readonly int _rollbackBuffer = configuration.GetValue("CardanoNodeConnection:RollbackBuffer", 10);
    private readonly ulong _networkMagic = configuration.GetValue("CardanoNodeConnection:NetworkMagic", 2UL);
    private readonly string _defaultStartHash = configuration.GetValue<string>("CardanoNodeConnection:Hash") ?? throw new InvalidOperationException("Default start hash not configured.");
    private readonly ulong _defaultStartSlot = configuration.GetValue<ulong?>("CardanoNodeConnection:Slot") ?? throw new InvalidOperationException("Default start slot not configured.");
    private readonly bool _rollbackModeEnabled = configuration.GetValue("Sync:Rollback:Enabled", false);
    private readonly bool _exitOnCompletion = configuration.GetValue("Sync:Worker:ExitOnCompletion", true);

    private readonly bool _tuiMode = configuration.GetValue("Sync:Dashboard:TuiMode", true);
    private readonly TimeSpan _dashboardRefreshInterval = TimeSpan.FromMilliseconds(Math.Max(configuration.GetValue("Sync:Dashboard:RefreshInterval", 1000), 2000));
    private readonly TimeSpan _dbSyncInterval = TimeSpan.FromMilliseconds(configuration.GetValue("Sync:State:ReducerStateSyncInterval", 10000));

    // --- LoggerMessage source-generated high-performance logging ---

    [LoggerMessage(Level = LogLevel.Information, Message = "Dependency graph built: {RootCount} root reducers, {TotalCount} total reducers")]
    private static partial void LogDependencyGraphBuilt(ILogger logger, int rootCount, int totalCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No root reducers found. All reducers have dependencies, which may indicate a circular dependency.")]
    private static partial void LogNoRootReducers(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting chain sync for {Count} root reducers: {Reducers}")]
    private static partial void LogStartingChainSync(ILogger logger, int count, string reducers);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get the initial intersection for {Reducer}")]
    private static partial void LogFailedInitialIntersection(ILogger logger, string reducer);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting chain sync for {Reducer} with {Count} intersection point(s). Slots: [{Slots}]")]
    private static partial void LogStartingReducerChainSync(ILogger logger, string reducer, int count, string slots);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rollback successfully completed. Please disable rollback mode to start syncing.")]
    private static partial void LogRollbackCompleted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reducer {Reducer} sync operation was cancelled.")]
    private static partial void LogReducerSyncCancelled(ILogger logger, string reducer);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error occurred while syncing reducer {Reducer}")]
    private static partial void LogReducerSyncError(ILogger logger, Exception ex, string reducer);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error forwarding {Action} to dependent reducer {Dependent} at slot {Slot}")]
    private static partial void LogForwardingError(ILogger logger, Exception ex, NextResponseAction action, string dependent, ulong slot);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dependency {Dependency} state not found for {Dependent}")]
    private static partial void LogDependencyStateNotFound(ILogger logger, string dependency, string dependent);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Dependent} and {Dependency} both at initial state, no adjustment needed")]
    private static partial void LogBothAtInitialState(ILogger logger, string dependent, string dependency);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Dependent} has processed blocks but dependency {Dependency} hasn't started yet")]
    private static partial void LogDependentAheadOfDependency(ILogger logger, string dependent, string dependency);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dependency {Dependency} has invalid latest intersection")]
    private static partial void LogInvalidDependencyIntersection(ILogger logger, string dependency);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adjusting {Dependent} start point from slot {OldSlot} to {NewSlot} (hash: {Hash}) to match dependency {Dependency}")]
    private static partial void LogAdjustingStartPoint(ILogger logger, string dependent, ulong oldSlot, ulong newSlot, string hash, string dependency);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Dependent} has already processed up to slot {DependentSlot} but dependency {Dependency} is only at slot {DependencySlot}. This indicates an inconsistent state!")]
    private static partial void LogInconsistentState(ILogger logger, string dependent, ulong dependentSlot, string dependency, ulong dependencySlot);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Dependent} is configured to start at slot {StartSlot}, will wait for dependency {Dependency} to reach this point (currently at {CurrentSlot})")]
    private static partial void LogWaitingForDependency(ILogger logger, string dependent, ulong startSlot, string dependency, ulong currentSlot);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Dependent} and {Dependency} are synchronized at slot {Slot}")]
    private static partial void LogSynchronizedAtSlot(ILogger logger, string dependent, string dependency, ulong slot);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamically adjusting {Dependent} start point from {OldSlot} to {NewSlot} as dependency {Dependency} has advanced significantly")]
    private static partial void LogDynamicAdjustment(ILogger logger, string dependent, ulong oldSlot, ulong newSlot, string dependency);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist reducer state for {Reducer}")]
    private static partial void LogPersistStateError(ILogger logger, Exception ex, string reducer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "State not found for reducer {Reducer}, skipping block at slot {Slot}")]
    private static partial void LogStateNotFound(ILogger logger, string reducer, ulong slot);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping block {Slot} for {Dependent} - dependency {Dependency} is only at slot {DepSlot}")]
    private static partial void LogSkippingBlock(ILogger logger, ulong slot, string dependent, string dependency, ulong depSlot);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load reducer state for {Reducer}")]
    private static partial void LogFailedToLoadReducerState(ILogger logger, Exception ex, string reducer);

    [LoggerMessage(Level = LogLevel.Information, Message = "Root reducer {Reducer} using {Count} intersection point(s) up to slot {Slot} (oldest dependent slot from chain of {ChainCount} reducers)")]
    private static partial void LogRootReducerIntersections(ILogger logger, string reducer, int count, ulong slot, int chainCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Root reducer {Reducer} has no intersections at or below oldest dependent slot {Slot}, using single fallback point")]
    private static partial void LogRootReducerFallback(ILogger logger, string reducer, ulong slot);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{Reducer}]: {Progress:F1}% - Avg {AvgMs:F1}ms, Slot {Slot}/{EffectiveTip}, Processed {Count}")]
    private static partial void LogTelemetryActive(ILogger logger, string reducer, double progress, double avgMs, ulong slot, ulong effectiveTip, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load reducer state for {ReducerName}")]
    private static partial void LogFailedToLoadReducerStateTelemetry(ILogger logger, Exception ex, string reducerName);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{Reducer}]: {Progress:F1}% - Slot {Slot}/{EffectiveTip} (waiting for blocks)")]
    private static partial void LogTelemetryWaiting(ILogger logger, string reducer, double progress, ulong slot, ulong effectiveTip);

    private void BuildDependencyGraph()
    {
        // Build reducer lookup and initialize dependency structures
        foreach (IReducer<IReducerModel> reducer in reducers)
        {
            string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            _reducersByName[reducerName] = reducer;
            _dependentReducers[reducerName] = [];
            _reducerDependency[reducerName] = null;
        }

        // Build dependency mappings
        foreach (IReducer<IReducerModel> reducer in reducers)
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

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Build dependency graph first
        BuildDependencyGraph();

        // Initialize state for all reducers (including dependents)
        await InitializeAllReducerStatesAsync(stoppingToken);

        if (!_rollbackModeEnabled)
        {
            // Start TUI dashboard if enabled
            _ = Task.Run(() => InitDashboardAsync(stoppingToken), stoppingToken);

            // Start telemetry aggregation task only if TUI mode is disabled
            if (!_tuiMode)
            {
                _ = Task.Run(() => StartTelemetryAggregationAsync(stoppingToken), stoppingToken);
            }

            // Start reducer state sync
            _ = Task.Run(async () => await StartReducerStateSync(stoppingToken), stoppingToken);
        }

        // Only start chain sync for root reducers (those with no dependencies)
        List<IReducer<IReducerModel>> rootReducerInstances = [.. _rootReducers.Select(name => _reducersByName[name])];

        if (rootReducerInstances.Count == 0)
        {
            LogNoRootReducers(logger);
            Exit();
            return;
        }

        string rootReducerNames = string.Join(", ", _rootReducers);
        LogStartingChainSync(logger, rootReducerInstances.Count, rootReducerNames);

        _ = await Task.WhenAny(rootReducerInstances.Select(reducer => StartReducerChainSyncAsync(reducer, stoppingToken)));

        Exit();
    }

    private async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        try
        {
            string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            ReducerState reducerState = await GetReducerStateAsync(reducerName, stoppingToken);
            _reducerStates[reducerName] = reducerState;

            IEnumerable<Point> intersections = GetSafeIntersectionPoints(reducerName);
            bool rollbackMode = _rollbackModeEnabled;

            if (reducerState is null)
            {
                LogFailedInitialIntersection(logger, reducerName);
                throw new InvalidOperationException($"Failed to determine chainsync intersection for {reducerName}");
            }

            if (_rollbackModeEnabled)
            {
                rollbackMode = configuration.GetValue($"CardanoIndexReducers:RollbackMode:Reducers:{reducerName}:Enabled", true);

                if (rollbackMode)
                {
                    string? defaultRollbackHash = configuration.GetValue<string>("CardanoIndexReducers:RollbackMode:RollbackHash");
                    ulong defaultRollbackSlot = configuration.GetValue<ulong>("CardanoIndexReducers:RollbackMode:Slot");
                    string? selfRollbackHash = configuration.GetValue<string>($"CardanoIndexReducers:RollbackMode:Reducers:{reducerName}:RollbackHash");
                    ulong selfRollbackSlot = configuration.GetValue<ulong>($"CardanoIndexReducers:RollbackMode:Reducers:{reducerName}:RollbackSlot");
                    string rollbackHash = selfRollbackHash ?? defaultRollbackHash ?? throw new InvalidOperationException("Rollback hash not configured");
                    ulong rollbackSlot = selfRollbackSlot != 0 ? selfRollbackSlot : defaultRollbackSlot != 0 ? defaultRollbackSlot : throw new InvalidOperationException("Rollback slot not configured");

                    Point rollbackIntersection = new(rollbackHash, rollbackSlot);
                    intersections = [rollbackIntersection];
                }
            }

            ICardanoChainProvider chainProvider = chainProviderFactory.CreateProvider();

            // Store provider for root reducers so we can reuse for tip queries
            if (_rootReducers.Contains(reducerName))
            {
                _rootReducerProviders[reducerName] = chainProvider;
            }

            // Log intersection points for debugging
            List<Point> intersectionList = [.. intersections];
            string slotsDisplay = string.Join(", ", intersectionList.OrderByDescending(p => p.Slot).Select(p => p.Slot));
            LogStartingReducerChainSync(logger, reducerName, intersectionList.Count, slotsDisplay);

            await foreach (NextResponse nextResponse in chainProvider.StartChainSyncAsync(intersectionList, _networkMagic, stoppingToken))
            {
                Task reducerTask = nextResponse.Action switch
                {
                    NextResponseAction.RollForward => ProcessRollforwardAsync(reducer, nextResponse),
                    NextResponseAction.RollBack => ProcessRollbackAsync(reducer, nextResponse, rollbackMode),
                    NextResponseAction.Await => throw new NotImplementedException(),
                    _ => throw new InvalidOperationException($"Next response error received. {nextResponse}"),
                };

                await reducerTask;

                if (rollbackMode)
                {
                    await UpdateReducerStatesAsync(stoppingToken);
                    LogRollbackCompleted(logger);
                    Exit();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            string cancelledReducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            LogReducerSyncCancelled(logger, cancelledReducerName);
        }
        catch (Exception ex)
        {
            string failedReducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            LogReducerSyncError(logger, ex, failedReducerName);
            throw;
        }
    }

    private async Task ProcessRollforwardAsync(IReducer<IReducerModel> reducer, NextResponse response)
    {
        if (_rollbackModeEnabled)
        {
            return;
        }

        string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
        Stopwatch stopwatch = Stopwatch.StartNew();

        await reducer.RollForwardAsync(response.Block!);

        stopwatch.Stop();
        ulong slot = response.Block!.Header().HeaderBody().Slot();

        // Send telemetry data non-blocking
        RecordTelemetry(reducerName, stopwatch.ElapsedMilliseconds, slot);

        Point recentIntersection = new(response.Block!.Header().Hash(), slot);
        IEnumerable<Point> latestIntersections = UpdateLatestIntersections(_reducerStates[reducerName].LatestIntersections, recentIntersection);
        _reducerStates[reducerName] = _reducerStates[reducerName] with
        {
            LatestIntersections = latestIntersections
        };

        // Forward to dependent reducers
        await ForwardToDependentsAsync(reducer, response, NextResponseAction.RollForward);
    }

    private async Task ProcessRollbackAsync(IReducer<IReducerModel> reducer, NextResponse response, bool rollbackMode)
    {
        if (_rollbackModeEnabled && !rollbackMode)
        {
            return;
        }

        string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
        ulong rollbackSlot = response.RollBackType switch
        {
            RollBackType.Exclusive => response.RollbackSlot!.Value + 1UL,
            RollBackType.Inclusive => response.RollbackSlot!.Value,
            _ => 0
        };

        long rollbackDepth = (long)_reducerStates[reducerName].LatestSlot - (long)rollbackSlot;

        if (rollbackDepth >= _maxRollbackSlots && !rollbackMode)
        {
            throw new InvalidOperationException($"Requested RollBack Slot {rollbackSlot} is more than {_maxRollbackSlots} slots behind current slot {_reducerStates[reducerName].LatestSlot}.");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        await reducer.RollBackwardAsync(rollbackSlot);

        stopwatch.Stop();

        // Record rollback telemetry
        RecordTelemetry(reducerName, stopwatch.ElapsedMilliseconds, rollbackSlot);

        IEnumerable<Point> latestIntersections = _reducerStates[reducerName].LatestIntersections;
        latestIntersections = latestIntersections.Where(i => i.Slot < rollbackSlot);
        _reducerStates[reducerName] = _reducerStates[reducerName] with
        {
            LatestIntersections = latestIntersections
        };

        // Forward rollback to dependent reducers
        await ForwardToDependentsAsync(reducer, response, NextResponseAction.RollBack);
    }

    private async Task ForwardToDependentsAsync(IReducer<IReducerModel> parentReducer, NextResponse response, NextResponseAction action)
    {
        string parentName = ArgusUtil.GetTypeNameWithoutGenerics(parentReducer.GetType());

        if (!_dependentReducers.TryGetValue(parentName, out List<string>? dependentNames) || dependentNames.Count == 0)
        {
            return; // No dependents to forward to
        }

        ulong slot = action == NextResponseAction.RollBack
            ? response.RollbackSlot!.Value
            : response.Block!.Header().HeaderBody().Slot();

        // Process all dependents in parallel
        IEnumerable<Task> tasks = dependentNames
            .Where(dependentName => ShouldProcessBlock(dependentName, slot))
            .Select(dependentName => ProcessDependentAsync(dependentName, response, action));

        await Task.WhenAll(tasks);
    }

    private async Task ProcessDependentAsync(string dependentName, NextResponse response, NextResponseAction action)
    {
        IReducer<IReducerModel> dependentReducer = _reducersByName[dependentName];
        ulong slot = action == NextResponseAction.RollBack
            ? response.RollbackSlot!.Value
            : response.Block!.Header().HeaderBody().Slot();

        try
        {
            // Dynamic runtime adjustment check
            await CheckAndAdjustDependentStartPointAsync(dependentName);

            Stopwatch stopwatch = Stopwatch.StartNew();

            if (action == NextResponseAction.RollForward)
            {
                await dependentReducer.RollForwardAsync(response.Block!);

                // Update dependent state
                if (_reducerStates.TryGetValue(dependentName, out ReducerState? currentState))
                {
                    Point recentIntersection = new(response.Block!.Header().Hash(), slot);
                    IEnumerable<Point> latestIntersections = UpdateLatestIntersections(currentState.LatestIntersections, recentIntersection);
                    _reducerStates[dependentName] = currentState with
                    {
                        LatestIntersections = latestIntersections
                    };
                }
            }
            else if (action == NextResponseAction.RollBack)
            {
                ulong rollbackSlot = response.RollBackType switch
                {
                    RollBackType.Exclusive => response.RollbackSlot!.Value + 1UL,
                    RollBackType.Inclusive => response.RollbackSlot!.Value,
                    _ => 0
                };

                await dependentReducer.RollBackwardAsync(rollbackSlot);

                // Update dependent state
                if (_reducerStates.TryGetValue(dependentName, out ReducerState? currentState))
                {
                    IEnumerable<Point> latestIntersections = currentState.LatestIntersections.Where(i => i.Slot < rollbackSlot);
                    _reducerStates[dependentName] = currentState with
                    {
                        LatestIntersections = latestIntersections
                    };
                }
            }

            stopwatch.Stop();
            RecordTelemetry(dependentName, stopwatch.ElapsedMilliseconds, slot);

            // Recursively forward to dependents of this dependent
            await ForwardToDependentsAsync(dependentReducer, response, action);
        }
        catch (Exception ex)
        {
            LogForwardingError(logger, ex, action, dependentName, slot);
            throw;
        }
    }

    private async Task InitializeAllReducerStatesAsync(CancellationToken stoppingToken)
    {
        // First, load all reducer states
        foreach ((string? reducerName, IReducer<IReducerModel> _) in _reducersByName)
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

    private async Task CheckAndAdjustDependentStartPointAsync(string dependentName)
    {
        // Only check periodically to avoid overhead
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string lastCheckKey = $"LastStartPointCheck_{dependentName}";

        if (_lastStartPointChecks.TryGetValue(lastCheckKey, out DateTimeOffset lastCheck))
        {
            // Check at most once per minute
            if ((now - lastCheck).TotalMinutes < 1)
            {
                return;
            }
        }

        _lastStartPointChecks[lastCheckKey] = now;

        // Perform the adjustment check
        string? dependency = _reducerDependency[dependentName];
        if (dependency == null)
        {
            return;
        }

        if (!_reducerStates.TryGetValue(dependency, out ReducerState? depState) ||
            !_reducerStates.TryGetValue(dependentName, out ReducerState? dependentState))
        {
            return;
        }

        // Check if dependency has significantly advanced since last adjustment
        if (depState.LatestIntersections.Any())
        {
            ulong depLatestSlot = depState.LatestSlot;

            // If dependent hasn't started yet but dependency has advanced significantly
            if (!dependentState.LatestIntersections.Any() &&
                dependentState.StartIntersection.Slot < depLatestSlot - 1000) // More than 1000 slots behind
            {
                Point? newStartPoint = depState.LatestIntersections
                    .OrderByDescending(p => p.Slot)
                    .Skip(5) // Use a slightly older intersection for safety
                    .FirstOrDefault();

                if (newStartPoint != null && newStartPoint.Slot > dependentState.StartIntersection.Slot)
                {
                    LogDynamicAdjustment(logger, dependentName, dependentState.StartIntersection.Slot, newStartPoint.Slot, dependency);

                    _reducerStates[dependentName] = dependentState with
                    {
                        StartIntersection = newStartPoint
                    };

                    // Persist the change
                    await PersistReducerStateAsync(dependentName);
                }
            }
        }
    }

    private async Task PersistReducerStateAsync(string reducerName)
    {
        if (!_reducerStates.TryGetValue(reducerName, out ReducerState? state))
        {
            return;
        }

        try
        {
            await using T dbContext = await dbContextFactory.CreateDbContextAsync();
            ReducerState? existingState = await dbContext.ReducerStates
                .FirstOrDefaultAsync(r => r.Name == reducerName);

            if (existingState != null)
            {
                existingState.StartIntersectionJson = state.StartIntersectionJson;
                existingState.LatestIntersectionsJson = state.LatestIntersectionsJson;
                _ = await dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            LogPersistStateError(logger, ex, reducerName);
        }
    }

    private bool ShouldProcessBlock(string reducerName, ulong blockSlot)
    {
        if (!_reducerStates.TryGetValue(reducerName, out ReducerState? state))
        {
            LogStateNotFound(logger, reducerName, blockSlot);
            return false;
        }

        // First check: Is this block before the reducer's start point?
        if (blockSlot < state.StartIntersection.Slot)
        {
            return false;
        }

        // For root reducers, no additional checks needed
        if (_rootReducers.Contains(reducerName))
        {
            return true;
        }

        // For dependent reducers, perform dynamic runtime check
        string? dependency = _reducerDependency[reducerName];
        if (dependency != null && _reducerStates.TryGetValue(dependency, out ReducerState? depState))
        {
            // Check if dependency has processed this block or beyond
            ulong dependencyLatestSlot = _latestSlots.TryGetValue(dependency, out ulong latestSlot)
                ? latestSlot
                : depState.StartIntersection.Slot;

            if (blockSlot > dependencyLatestSlot)
            {
                LogSkippingBlock(logger, blockSlot, reducerName, dependency, dependencyLatestSlot);
                return false;
            }
        }

        return true;
    }

    private async Task<ReducerState> GetReducerStateAsync(string reducerName, CancellationToken stoppingToken)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync(stoppingToken);

        ReducerState? state = await dbContext.ReducerStates
            .AsNoTracking()
            .Where(s => s.Name == reducerName)
            .FirstOrDefaultAsync(cancellationToken: stoppingToken);

        if (state is not null)
        {
            if (!state.LatestIntersections.Any())
            {
                state = state with
                {
                    LatestIntersections = [state.StartIntersection]
                };
            }
        }

        ReducerState initialState = GetDefaultReducerState(reducerName);

        return state ?? initialState;
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

    private async Task UpdateReducerStatesAsync(CancellationToken stoppingToken)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync(stoppingToken);

        IEnumerable<ReducerState> newStates = _reducerStates.Values;
        IEnumerable<string> reducerNames = newStates.Select(ns => ns.Name);
        IEnumerable<ReducerState> reducerStates = await dbContext.ReducerStates
            .Where(rs => reducerNames.Contains(rs.Name))
            .ToListAsync(cancellationToken: stoppingToken);

        foreach (ReducerState newState in newStates)
        {
            ReducerState? existingState = reducerStates.FirstOrDefault(rs => rs.Name == newState.Name);
            if (existingState is not null)
            {
                existingState.LatestIntersections = newState.LatestIntersections;
            }
            else
            {
                _ = dbContext.ReducerStates.Add(newState);
            }
        }

        _ = await dbContext.SaveChangesAsync(stoppingToken);
    }

    private IEnumerable<Point> UpdateLatestIntersections(IEnumerable<Point> latestIntersections, Point newIntersection)
    {
        // Always add the new intersection first
        IEnumerable<Point> updated = latestIntersections.Append(newIntersection);

        // Order by slot descending (newest first)
        updated = updated.OrderByDescending(i => i.Slot);

        // Keep only the most recent _rollbackBuffer intersections
        if (updated.Count() > _rollbackBuffer)
        {
            updated = updated.Take(_rollbackBuffer);
        }

        return updated;
    }

    private async Task InitDashboardAsync(CancellationToken stoppingToken)
    {
        if (_tuiMode)
        {
            await Task.Delay(500, stoppingToken);
            if (configuration.GetValue<string>("Sync:Dashboard:DisplayType") == "Full")
            {
                _ = Task.Run(() => StartSyncFullDashboardTracker(stoppingToken), stoppingToken);
            }
            else
            {
                _ = Task.Run(() => StartSyncProgressTrackerAsync(stoppingToken), stoppingToken);
            }
        }

        await Task.CompletedTask;
    }

    private async Task StartSyncProgressTrackerAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(100, cancellationToken);

        await AnsiConsole.Progress()
            .Columns(
            [
                new TaskDescriptionColumn(),
                    new ProgressBarColumn()
                    {
                        CompletedStyle = Color.MediumSpringGreen,
                        FinishedStyle = Color.Cyan2
                    },
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn(),
            ])
            .StartAsync(async ctx =>
            {
                Dictionary<ProgressTask, string> taskToReducerName = [];
                List<ProgressTask> tasks = [];
                foreach (IReducer<IReducerModel> reducer in reducers)
                {
                    string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
                    ProgressTask task = ctx.AddTask(reducerName);
                    tasks.Add(task);
                    taskToReducerName[task] = reducerName;
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Update effective tip to maximum processed slot across all reducers

                    foreach (ProgressTask task in tasks)
                    {
                        string reducerName = taskToReducerName[task];

                        // Load reducer state if not available
                        if (!_reducerStates.ContainsKey(reducerName))
                        {
                            try
                            {
                                ReducerState reducerState = await GetReducerStateAsync(reducerName, cancellationToken);
                                _reducerStates[reducerName] = reducerState;
                            }
                            catch (Exception ex)
                            {
                                LogFailedToLoadReducerState(logger, ex, reducerName);
                                continue;
                            }
                        }

                        ReducerState state = _reducerStates[reducerName];
                        ulong startSlot = state.StartIntersection.Slot;

                        // Get tip from the appropriate root reducer's provider
                        ulong currentTip = EffectiveTipSlot;
                        string? rootName = GetRootReducerName(reducerName);
                        if (rootName != null && _rootReducerProviders.TryGetValue(rootName, out ICardanoChainProvider? rootProvider))
                        {
                            try
                            {
                                Point tip = await rootProvider.GetTipAsync();
                                currentTip = tip.Slot;
                                // Update global effective tip
                                EffectiveTipSlot = Math.Max(EffectiveTipSlot, currentTip);
                            }
                            catch
                            {
                                // Fall back to EffectiveTipSlot if query fails
                            }
                        }

                        // Calculate progress based on reducer type
                        double progress = CalculateReducerProgress(reducerName, state);
                        task.Value = progress;

                        // Update display with slot information
                        ulong displaySlot = _latestSlots.TryGetValue(reducerName, out ulong latestSlot) ? latestSlot : state.StartIntersection.Slot;
                        task.Description = $"{reducerName} ({displaySlot}/{currentTip})";
                    }

                    // Wait for next refresh interval
                    // Wait for new block/slot updates or timeout for periodic refresh
                    _ = await _tuiUpdateSignal.WaitAsync(TimeSpan.FromMilliseconds(1000), cancellationToken);
                }
            }
        );
    }


    private async Task StartSyncFullDashboardTracker(CancellationToken cancellationToken)
    {
        Layout layout = new Layout("Main")
            .SplitRows(
                new Layout("OverallSyncProgress"),
                new Layout("Performance")
                    .SplitColumns(
                        new Layout("SyncProgress"),
                        new Layout("MemoryBenchmark")
                    )
            );

        await AnsiConsole.Live(layout)
            .StartAsync(async ctx =>
            {
                Process processInfo = Process.GetCurrentProcess();

                // Sync Progress
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Update effective tip to maximum processed slot across all reducers
                    if (_latestSlots.Values.Count > 0)
                    {
                        EffectiveTipSlot = Math.Max(EffectiveTipSlot, _latestSlots.Values.Max());
                    }

                    BarChart syncBarChart = new BarChart()
                        .WithMaxValue(100.0)
                        .Label("[darkorange3 bold underline]Syncing...[/]")
                        .LeftAlignLabel();

                    double overallProgress = 0.0;
                    int validReducerCount = 0;

                    foreach (IReducer<IReducerModel> reducer in reducers)
                    {
                        string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());

                        // Load reducer state if not available
                        if (!_reducerStates.ContainsKey(reducerName))
                        {
                            try
                            {
                                ReducerState reducerState = await GetReducerStateAsync(reducerName, cancellationToken);
                                _reducerStates[reducerName] = reducerState;
                            }
                            catch (Exception ex)
                            {
                                LogFailedToLoadReducerState(logger, ex, reducerName);
                                continue;
                            }
                        }

                        ReducerState state = _reducerStates[reducerName];

                        // Calculate progress based on reducer type
                        double progress = CalculateReducerProgress(reducerName, state);

                        // Get current slot for this reducer
                        ulong currentSlot = _latestSlots.TryGetValue(reducerName, out ulong latestSlot) ? latestSlot : state.StartIntersection.Slot;
                        string displayName = $"{state.Name} ({currentSlot}/{EffectiveTipSlot})";

                        _ = syncBarChart.AddItem(displayName, Math.Round(progress, 2), GetProgressColor(progress));
                        overallProgress += progress;
                        validReducerCount++;
                    }

                    // Overall Progress
                    if (validReducerCount > 0)
                    {
                        overallProgress /= validReducerCount;
                    }
                    FigletText progressText = new FigletText(FigletFont.Default, $"{Math.Round(overallProgress, 0)}%")
                        .Centered()
                        .Color(Color.Blue);

                    Panel overallSyncPanel = new(progressText)
                    {
                        Border = BoxBorder.Rounded,
                        Expand = true,
                        Header = new PanelHeader("Overall Sync Progress", Justify.Center)
                    };

                    // Update memory and CPU statistics
                    DateTime lastCpuCheck = DateTime.Now;
                    TimeSpan lastCpuTotal = processInfo.TotalProcessorTime;
                    processInfo.Refresh();

                    // Get memory usage
                    double memoryUsedMB = processInfo.WorkingSet64 / (1024.0 * 1024.0);
                    double privateMemoryMB = processInfo.PrivateMemorySize64 / (1024.0 * 1024.0);
                    double managedMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

                    // Calculate CPU usage
                    DateTime currentTime = DateTime.Now;
                    TimeSpan currentCpuTime = processInfo.TotalProcessorTime;

                    double cpuUsage = 0;
                    if (lastCpuTotal != TimeSpan.Zero)
                    {
                        TimeSpan cpuUsedSinceLastCheck = currentCpuTime - lastCpuTotal;
                        TimeSpan timePassed = currentTime - lastCpuCheck;
                        cpuUsage = cpuUsedSinceLastCheck.TotalMilliseconds /
                                  (Environment.ProcessorCount * timePassed.TotalMilliseconds) * 100;
                        cpuUsage = Math.Min(100, Math.Max(0, cpuUsage)); // Ensure it's between 0-100
                    }

                    // Store current values for next calculation
                    lastCpuCheck = currentTime;
                    lastCpuTotal = currentCpuTime;

                    // Create Memory Bar Chart
                    BarChart memoryChart = new BarChart()
                        .Label("[bold underline]Memory Usage (MB)[/]")
                        .CenterLabel()
                        .WithMaxValue(GetEstimatedMaxMemory(memoryUsedMB, privateMemoryMB, managedMemoryMB))
                        .AddItem("Working", Math.Round(memoryUsedMB, 1), Color.Green)
                        .AddItem("Private", Math.Round(privateMemoryMB, 1), Color.Yellow)
                        .AddItem("Managed", Math.Round(managedMemoryMB, 1), Color.Blue);

                    // Create CPU Bar Chart
                    BarChart cpuChart = new BarChart()
                        .Label("[bold underline]CPU Usage (%)[/]")
                        .CenterLabel()
                        .WithMaxValue(100)
                        .AddItem("Current", Math.Round(cpuUsage, 1), CardanoIndexWorker<T>.GetCpuColor(cpuUsage));

                    // Create the combined panel
                    Panel systemMonitorPanel = new(
                        new Rows(
                            memoryChart,
                            new Rule("[bold]System Stats[/]"),
                            cpuChart,
                            new Markup($"[bold]Threads: [cyan]{processInfo.Threads.Count}[/] | Handles: [magenta]{processInfo.HandleCount}[/][/]")
                        )
                    )
                    {
                        Border = BoxBorder.Rounded,
                        Padding = new Padding(1, 0, 1, 0),
                        Header = new PanelHeader("System Resources", Justify.Center)
                    };

                    // Update the layouts
                    _ = layout["MemoryBenchmark"].Update(systemMonitorPanel);
                    _ = layout["SyncProgress"].Update(syncBarChart);
                    _ = layout["OverallSyncProgress"].Update(overallSyncPanel);

                    ctx.Refresh();

                    // Wait for next refresh interval
                    // Wait for new block/slot updates or timeout for periodic refresh
                    _ = await _tuiUpdateSignal.WaitAsync(TimeSpan.FromMilliseconds(1000), cancellationToken);
                }
            });
    }

    private static Color GetCpuColor(double percentage)
    {
        if (percentage < 30)
        {
            return Color.Green;
        }

        if (percentage < 70)
        {
            return Color.Yellow;
        }

        return Color.Red;
    }

    private static double GetEstimatedMaxMemory(double workingSetMB, double privateMB, double managedMB)
    {
        try
        {
            GCMemoryInfo memInfo = GC.GetGCMemoryInfo();
            double totalPhysicalMemoryMB = memInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0);
            double currentMax = Math.Max(Math.Max(workingSetMB, privateMB), managedMB);
            return Math.Max(totalPhysicalMemoryMB, currentMax * 2);
        }
        catch
        {
            double currentMax = Math.Max(Math.Max(workingSetMB, privateMB), managedMB);
            return currentMax * 1.5;
        }
    }

    private static Color GetProgressColor(double value) =>
         value switch
         {
             <= 20 => Color.Red3_1,
             <= 40 => Color.DarkOrange3_1,
             <= 60 => Color.Orange3,
             <= 80 => Color.DarkSeaGreen,
             _ => Color.MediumSpringGreen
         };

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

    private double CalculateReducerProgress(string reducerName, ReducerState state)
    {
        // For dependent reducers, recursively calculate based on root dependency's progress
        if (_reducerDependency.TryGetValue(reducerName, out string? dependency) && dependency != null)
        {
            // Get dependency's state
            if (_reducerStates.TryGetValue(dependency, out ReducerState? depState))
            {
                // Recursively calculate dependency's progress (handles chains)
                return CalculateReducerProgress(dependency, depState);
            }
        }

        // For root reducers, use standard calculation
        ulong startSlot = state.StartIntersection.Slot;
        ulong currentSlot = _latestSlots.TryGetValue(reducerName, out ulong latestSlot)
            ? latestSlot
            : state.LatestIntersections.MaxBy(p => p.Slot)?.Slot ?? startSlot;

        if (EffectiveTipSlot <= startSlot)
        {
            return 100.0;
        }
        else if (currentSlot >= EffectiveTipSlot)
        {
            return 100.0;
        }
        else
        {
            ulong totalSlotsToSync = EffectiveTipSlot - startSlot;
            ulong totalSlotsSynced = currentSlot >= startSlot ? currentSlot - startSlot : 0;
            double progress = (double)totalSlotsSynced / totalSlotsToSync * 100.0;

            // Ensure we never show 100% unless actually at tip
            return Math.Min(progress, 99.99);
        }
    }


    private async Task StartReducerStateSync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await UpdateReducerStatesAsync(stoppingToken);
            await Task.Delay(_dbSyncInterval, stoppingToken);
        }
    }


    private async Task StartTelemetryAggregationAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Update effective tip to maximum processed slot across all reducers
            if (_latestSlots.Values.Count > 0)
            {
                EffectiveTipSlot = Math.Max(EffectiveTipSlot, _latestSlots.Values.Max());
            }

            // Get all reducer names (from telemetry or from configured reducers)
            HashSet<string> allReducerNames = [];
            foreach (IReducer<IReducerModel> reducer in reducers)
            {
                _ = allReducerNames.Add(ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType()));
            }

            foreach (string reducerName in allReducerNames)
            {
                // Check if we have recent telemetry data
                long[] times = [.. _processingTimes.GetValueOrDefault(reducerName, [])];
                bool hasRecentActivity = times.Length > 0;

                if (hasRecentActivity)
                {
                    // Show telemetry-based logs
                    double avgTime = times.Average();
                    ulong latestSlot = _latestSlots.TryGetValue(reducerName, out ulong slot) ? slot : 0;

                    // Get start slot from reducer state
                    ReducerState? reducerState = _reducerStates.GetValueOrDefault(reducerName);
                    ulong startSlot = reducerState?.StartIntersection.Slot ?? 0;

                    double progress = reducerState != null
                        ? CalculateReducerProgress(reducerName, reducerState)
                        : 0.0;

                    LogTelemetryActive(logger, reducerName, progress, avgTime, latestSlot, EffectiveTipSlot, times.Length);

                    _processingTimes[reducerName].Clear();
                }
                else
                {
                    // No recent activity, check reducer state from memory or load from database
                    ReducerState? reducerState = _reducerStates.GetValueOrDefault(reducerName);
                    if (reducerState == null)
                    {
                        // Load reducer state from database if not in memory yet
                        try
                        {
                            reducerState = await GetReducerStateAsync(reducerName, stoppingToken);
                            _reducerStates[reducerName] = reducerState;
                        }
                        catch (Exception ex)
                        {
                            LogFailedToLoadReducerStateTelemetry(logger, ex, reducerName);
                            continue;
                        }
                    }

                    if (reducerState != null)
                    {
                        ulong currentSlot = reducerState.LatestIntersections.MaxBy(p => p.Slot)?.Slot ?? reducerState.StartIntersection.Slot;
                        ulong startSlot = reducerState.StartIntersection.Slot;

                        double progress = CalculateReducerProgress(reducerName, reducerState);

                        LogTelemetryWaiting(logger, reducerName, progress, currentSlot, EffectiveTipSlot);
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private void RecordTelemetry(string reducerName, long elapsedMs, ulong slot)
    {
        _ = _processingTimes.AddOrUpdate(reducerName,
            [elapsedMs],
            (key, list) => { list.Add(elapsedMs); return list; });

        _latestSlots[reducerName] = slot;

        // Signal TUI update for real-time display with anti-spam protection
        // Use 1/4 of dashboard refresh interval to allow up to 4 updates per refresh cycle
        TimeSpan antiSpamInterval = TimeSpan.FromMilliseconds(_dashboardRefreshInterval.TotalMilliseconds / 4);
        TimeSpan timeSinceLastUpdate = DateTime.UtcNow - _lastTuiUpdate;

        if (timeSinceLastUpdate >= antiSpamInterval)
        {
            try
            {
                _ = _tuiUpdateSignal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Semaphore is already at max count, ignore
            }
            _lastTuiUpdate = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Conditionally exits the application based on configuration.
    /// In production (ExitOnCompletion=true), terminates the process.
    /// In testing (ExitOnCompletion=false), allows graceful completion.
    /// </summary>
    private void Exit()
    {
        if (_exitOnCompletion)
        {
            Environment.Exit(0);
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _tuiUpdateSignal.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
