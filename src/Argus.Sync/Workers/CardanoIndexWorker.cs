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
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using System.Diagnostics;

namespace Argus.Sync.Workers;

public class CardanoIndexWorker<T>(
    IConfiguration configuration,
    ILogger<CardanoIndexWorker<T>> logger,
    IDbContextFactory<T> dbContextFactory,
    IEnumerable<IReducer<IReducerModel>> reducers,
    IChainProviderFactory chainProviderFactory
) : BackgroundService where T : CardanoDbContext
{
    private readonly ConcurrentDictionary<string, ReducerState> _reducerStates = [];
    private ulong EffectiveTipSlot = 0;
    private readonly ConcurrentDictionary<string, List<long>> _processingTimes = [];
    private readonly ConcurrentDictionary<string, ulong> _latestSlots = [];
    
    // Dependency graph structures
    private readonly Dictionary<string, IReducer<IReducerModel>> _reducersByName = [];
    private readonly Dictionary<string, List<string>> _dependentReducers = []; // parent -> dependents
    private readonly Dictionary<string, string?> _reducerDependency = []; // reducer -> its single dependency
    private readonly HashSet<string> _rootReducers = [];

    private readonly long _maxRollbackSlots = configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000);
    private readonly int _rollbackBuffer = configuration.GetValue("CardanoNodeConnection:RollbackBuffer", 10);
    private readonly ulong _networkMagic = configuration.GetValue("CardanoNodeConnection:NetworkMagic", 2UL);
    private readonly string _defaultStartHash = configuration.GetValue<string>("CardanoNodeConnection:Hash") ?? throw new Exception("Default start hash not configured.");
    private readonly ulong _defaultStartSlot = configuration.GetValue<ulong?>("CardanoNodeConnection:Slot") ?? throw new Exception("Default start slot not configured.");
    private readonly bool _rollbackModeEnabled = configuration.GetValue("Sync:Rollback:Enabled", false);
    private readonly bool _exitOnCompletion = configuration.GetValue("Sync:Worker:ExitOnCompletion", true);

    private readonly bool _tuiMode = configuration.GetValue("Sync:Dashboard:TuiMode", true);
    private readonly TimeSpan _dashboardRefreshInterval = TimeSpan.FromMilliseconds(Math.Max(configuration.GetValue("Sync:Dashboard:RefreshInterval", 1000), 2000));
    private readonly TimeSpan _dbSyncInterval = TimeSpan.FromMilliseconds(configuration.GetValue("Sync:State:ReducerStateSyncInterval", 10000));

    private void BuildDependencyGraph()
    {
        // Build reducer lookup and initialize dependency structures
        foreach (var reducer in reducers)
        {
            var reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            _reducersByName[reducerName] = reducer;
            _dependentReducers[reducerName] = [];
            _reducerDependency[reducerName] = null;
        }

        // Build dependency mappings
        foreach (var reducer in reducers)
        {
            var reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            var dependency = ReducerDependencyResolver.GetReducerDependency(reducer.GetType());
            
            if (dependency != null)
            {
                var dependencyName = ArgusUtil.GetTypeNameWithoutGenerics(dependency);
                
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
        foreach (var reducerName in _reducersByName.Keys)
        {
            if (_reducerDependency[reducerName] == null)
            {
                _rootReducers.Add(reducerName);
            }
        }

        logger.LogInformation("Dependency graph built: {RootCount} root reducers, {TotalCount} total reducers", 
            _rootReducers.Count, _reducersByName.Count);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Build dependency graph first
        BuildDependencyGraph();
        
        // Initialize state for all reducers (including dependents)
        await InitializeAllReducerStatesAsync(stoppingToken);
        
        if (!_rollbackModeEnabled)
        {
            // Start initial tip query
            _ = Task.Run(() => StartInitialTipQueryAsync(stoppingToken), stoppingToken);
            
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
        var rootReducerInstances = _rootReducers.Select(name => _reducersByName[name]).ToList();
        
        if (rootReducerInstances.Count == 0)
        {
            logger.LogWarning("No root reducers found. All reducers have dependencies, which may indicate a circular dependency.");
            Exit();
            return;
        }

        logger.LogInformation("Starting chain sync for {Count} root reducers: {Reducers}", 
            rootReducerInstances.Count, string.Join(", ", _rootReducers));

        await Task.WhenAny(rootReducerInstances.Select(reducer => StartReducerChainSyncAsync(reducer, stoppingToken)));

        Exit();
    }

    private async Task StartReducerChainSyncAsync(IReducer<IReducerModel> reducer, CancellationToken stoppingToken)
    {
        try
        {
            string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            ReducerState reducerState = await GetReducerStateAsync(reducerName, stoppingToken);
            _reducerStates[reducerName] = reducerState;

            IEnumerable<Point> intersections = reducerState.LatestIntersections;
            bool rollbackMode = _rollbackModeEnabled;

            if (reducerState is null)
            {
                logger.LogError("Failed to get the initial intersection for {Reducer}", reducerName);
                throw new Exception($"Failed to determine chainsync intersection for {reducerName}");
            }

            if (_rollbackModeEnabled)
            {
                rollbackMode = configuration.GetValue($"CardanoIndexReducers:RollbackMode:Reducers:{reducerName}:Enabled", true);

                if (rollbackMode)
                {
                    string? defaultRollbackHash = configuration.GetValue<string>("CardanoIndexReducers:RollbackMode:RollbackHash");
                    ulong? defaultRollbackSlot = configuration.GetValue<ulong>("CardanoIndexReducers:RollbackMode:Slot");
                    string? selfRollbackHash = configuration.GetValue<string>($"CardanoIndexReducers:RollbackMode:Reducers:{reducerName}:RollbackHash");
                    ulong? selfRollbackSlot = configuration.GetValue<ulong>($"CardanoIndexReducers:RollbackMode:Reducers:{reducerName}:RollbackSlot");
                    string rollbackHash = selfRollbackHash ?? defaultRollbackHash ?? throw new Exception("Rollback hash not configured");
                    ulong rollbackSlot = selfRollbackSlot ?? defaultRollbackSlot ?? throw new Exception("Rollback slot not configured");

                    Point rollbackIntersection = new(rollbackHash, rollbackSlot);
                    intersections = [rollbackIntersection];
                }
            }

            ICardanoChainProvider chainProvider = chainProviderFactory.CreateProvider();
            await foreach (NextResponse nextResponse in chainProvider.StartChainSyncAsync(intersections, _networkMagic, stoppingToken))
            {
                Task reducerTask = nextResponse.Action switch
                {
                    NextResponseAction.RollForward => ProcessRollforwardAsync(reducer, nextResponse),
                    NextResponseAction.RollBack => ProcessRollbackAsync(reducer, nextResponse, rollbackMode, stoppingToken),
                    _ => throw new Exception($"Next response error received. {nextResponse}"),
                };

                await reducerTask;

                if (rollbackMode)
                {
                    await UpdateReducerStatesAsync(stoppingToken);
                    logger.LogInformation("Rollback successfully completed. Please disable rollback mode to start syncing.");
                    Exit();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Reducer {Reducer} sync operation was cancelled.", ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType()));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while syncing reducer {Reducer}", ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType()));
            throw;
        }
    }

    private async Task ProcessRollforwardAsync(IReducer<IReducerModel> reducer, NextResponse response)
    {
        if (_rollbackModeEnabled) return;

        string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
        var stopwatch = Stopwatch.StartNew();
        
        await reducer.RollForwardAsync(response.Block);
        
        stopwatch.Stop();
        ulong slot = response.Block.Header().HeaderBody().Slot();
        
        // Send telemetry data non-blocking
        RecordTelemetry(reducerName, stopwatch.ElapsedMilliseconds, slot);

        Point recentIntersection = new(response.Block.Header().Hash(), slot);
        IEnumerable<Point> latestIntersections = UpdateLatestIntersections(_reducerStates[reducerName].LatestIntersections, recentIntersection);
        _reducerStates[reducerName] = _reducerStates[reducerName] with
        {
            LatestIntersections = latestIntersections
        };
        
        // Forward to dependent reducers
        await ForwardToDependentsAsync(reducer, response, NextResponseAction.RollForward);
    }

    private async Task ProcessRollbackAsync(IReducer<IReducerModel> reducer, NextResponse response, bool rollbackMode, CancellationToken stoppingToken)
    {
        if (_rollbackModeEnabled && !rollbackMode) return;

        string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
        ulong rollbackSlot = response.RollBackType switch
        {
            RollBackType.Exclusive => response.Block.Header().HeaderBody().Slot() + 1UL,
            RollBackType.Inclusive => response.Block.Header().HeaderBody().Slot(),
            _ => 0
        };

        long rollbackDepth = (long)_reducerStates[reducerName].LatestSlot - (long)rollbackSlot;

        if (rollbackDepth >= _maxRollbackSlots && !rollbackMode)
        {
            throw new Exception($"Requested RollBack Slot {rollbackSlot} is more than {_maxRollbackSlots} slots behind current slot {_reducerStates[reducerName].LatestSlot}.");
        }

        var stopwatch = Stopwatch.StartNew();
        
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
        var parentName = ArgusUtil.GetTypeNameWithoutGenerics(parentReducer.GetType());
        
        if (!_dependentReducers.TryGetValue(parentName, out var dependentNames) || dependentNames.Count == 0)
        {
            return; // No dependents to forward to
        }

        var slot = response.Block.Header().HeaderBody().Slot();
        
        // Process all dependents in parallel
        var tasks = dependentNames
            .Where(dependentName => ShouldProcessBlock(dependentName, slot))
            .Select(dependentName => ProcessDependentAsync(dependentName, response, action));
        
        await Task.WhenAll(tasks);
    }
    
    private async Task ProcessDependentAsync(string dependentName, NextResponse response, NextResponseAction action)
    {
        var dependentReducer = _reducersByName[dependentName];
        var slot = response.Block.Header().HeaderBody().Slot();
        
        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            if (action == NextResponseAction.RollForward)
            {
                await dependentReducer.RollForwardAsync(response.Block);
                
                // Update dependent state
                if (_reducerStates.TryGetValue(dependentName, out var currentState))
                {
                    Point recentIntersection = new(response.Block.Header().Hash(), slot);
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
                    RollBackType.Exclusive => response.Block.Header().HeaderBody().Slot() + 1UL,
                    RollBackType.Inclusive => response.Block.Header().HeaderBody().Slot(),
                    _ => 0
                };
                
                await dependentReducer.RollBackwardAsync(rollbackSlot);
                
                // Update dependent state
                if (_reducerStates.TryGetValue(dependentName, out var currentState))
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
            logger.LogError(ex, "Error forwarding {Action} to dependent reducer {Dependent} at slot {Slot}", 
                action, dependentName, slot);
            throw;
        }
    }

    private async Task InitializeAllReducerStatesAsync(CancellationToken stoppingToken)
    {
        foreach (var (reducerName, reducer) in _reducersByName)
        {
            var state = await GetReducerStateAsync(reducerName, stoppingToken);
            _reducerStates[reducerName] = state;
            
            // For dependent reducers, adjust their start point based on parent's current state
            if (!_rootReducers.Contains(reducerName))
            {
                await AdjustDependentStartPointAsync(reducerName, stoppingToken);
            }
        }
    }
    
    private Task AdjustDependentStartPointAsync(string dependentName, CancellationToken stoppingToken)
    {
        var dependency = _reducerDependency[dependentName];
        if (dependency == null) return Task.CompletedTask;
        
        // Get the slot of the single dependency
        ulong dependencySlot = 0;
        
        if (_reducerStates.TryGetValue(dependency, out var depState))
        {
            dependencySlot = depState.LatestSlot;
        }
        
        if (dependencySlot == 0) return Task.CompletedTask;
        
        var dependentState = _reducerStates[dependentName];
        
        // If dependent is behind its dependency, update its start point
        if (dependentState.StartIntersection.Slot < dependencySlot)
        {
            logger.LogInformation("Adjusting {Dependent} start point from {OldSlot} to {NewSlot} to match dependency", 
                dependentName, dependentState.StartIntersection.Slot, dependencySlot);
                
            // Create new start intersection at the dependency slot
            var newStartIntersection = new Point("", dependencySlot);
            _reducerStates[dependentName] = dependentState with
            {
                StartIntersection = newStartIntersection
            };
        }
        // If dependent is ahead, it will wait until dependency catches up
        else if (dependentState.StartIntersection.Slot > dependencySlot)
        {
            logger.LogInformation("{Dependent} is configured to start at slot {StartSlot}, will wait for dependency to reach this point (currently at {CurrentSlot})", 
                dependentName, dependentState.StartIntersection.Slot, dependencySlot);
        }
        
        return Task.CompletedTask;
    }
    
    private bool ShouldProcessBlock(string reducerName, ulong blockSlot)
    {
        if (!_reducerStates.TryGetValue(reducerName, out var state))
        {
            logger.LogWarning("State not found for reducer {Reducer}, skipping block at slot {Slot}", reducerName, blockSlot);
            return false;
        }

        // Check if this dependent is configured to start at or before this slot
        return blockSlot >= state.StartIntersection.Slot;
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
                dbContext.ReducerStates.Add(newState);
            }
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private IEnumerable<Point> UpdateLatestIntersections(IEnumerable<Point> latestIntersections, Point newIntersection)
    {
        latestIntersections = latestIntersections.OrderByDescending(i => i.Slot);
        if (latestIntersections.Count() >= _rollbackBuffer)
        {
            latestIntersections = latestIntersections.SkipLast(1);
        }
        else
        {
            latestIntersections = latestIntersections.Append(newIntersection);
        }

        return latestIntersections;
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
                List<ProgressTask> tasks = [.. reducers.Select(reducer =>
                    {
                        string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
                        return ctx.AddTask(reducerName);
                    })];

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Update effective tip to maximum processed slot across all reducers
                    if (_latestSlots.Values.Count > 0)
                    {
                        EffectiveTipSlot = Math.Max(EffectiveTipSlot, _latestSlots.Values.Max());
                    }

                    foreach (ProgressTask task in tasks)
                    {
                        string reducerName = task.Description;
                        
                        // Load reducer state if not available
                        if (!_reducerStates.ContainsKey(reducerName))
                        {
                            try
                            {
                                var reducerState = await GetReducerStateAsync(reducerName, cancellationToken);
                                _reducerStates[reducerName] = reducerState;
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to load reducer state for {Reducer}", reducerName);
                                continue;
                            }
                        }

                        ReducerState state = _reducerStates[reducerName];
                        ulong startSlot = state.StartIntersection.Slot;
                        
                        // Use latest processed slot or latest intersection
                        ulong currentSlot = _latestSlots.TryGetValue(reducerName, out var latestSlot) 
                            ? latestSlot 
                            : state.LatestIntersections.MaxBy(p => p.Slot)?.Slot ?? startSlot;

                        if (EffectiveTipSlot <= startSlot)
                        {
                            task.Value = 100.0;
                        }
                        else
                        {
                            ulong totalSlotsToSync = EffectiveTipSlot - startSlot;
                            ulong totalSlotsSynced = currentSlot >= startSlot ? currentSlot - startSlot : 0;
                            double progress = (double)totalSlotsSynced / totalSlotsToSync * 100.0;
                            task.Value = progress;
                        }
                    }

                    // Wait for next refresh interval
                    await Task.Delay(_dashboardRefreshInterval, cancellationToken);
                }
            }
        );
    }


    private async Task StartSyncFullDashboardTracker(CancellationToken cancellationToken)
    {
        var layout = new Layout("Main")
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
                    
                    foreach (var reducer in reducers)
                    {
                        string reducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
                        
                        // Load reducer state if not available
                        if (!_reducerStates.ContainsKey(reducerName))
                        {
                            try
                            {
                                var reducerState = await GetReducerStateAsync(reducerName, cancellationToken);
                                _reducerStates[reducerName] = reducerState;
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to load reducer state for {Reducer}", reducerName);
                                continue;
                            }
                        }
                        
                        ReducerState state = _reducerStates[reducerName];
                        ulong startSlot = state.StartIntersection.Slot;
                        
                        // Use latest processed slot or latest intersection
                        ulong currentSlot = _latestSlots.TryGetValue(reducerName, out var latestSlot) 
                            ? latestSlot 
                            : state.LatestIntersections.MaxBy(p => p.Slot)?.Slot ?? startSlot;

                        double progress;
                        if (EffectiveTipSlot <= startSlot)
                        {
                            progress = 100.0;
                        }
                        else
                        {
                            ulong totalSlotsToSync = EffectiveTipSlot - startSlot;
                            ulong totalSlotsSynced = currentSlot >= startSlot ? currentSlot - startSlot : 0;
                            progress = (double)totalSlotsSynced / totalSlotsToSync * 100.0;
                        }

                        syncBarChart.AddItem(state.Name, Math.Round(progress, 2), GetProgressColor(progress));
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
                    layout["MemoryBenchmark"].Update(systemMonitorPanel);
                    layout["SyncProgress"].Update(syncBarChart);
                    layout["OverallSyncProgress"].Update(overallSyncPanel);

                    ctx.Refresh();

                    // Wait for next refresh interval
                    await Task.Delay(_dashboardRefreshInterval, cancellationToken);
                }
            });
    }

    private static Color GetCpuColor(double percentage)
    {
        if (percentage < 30) return Color.Green;
        if (percentage < 70) return Color.Yellow;
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


    private async Task StartReducerStateSync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await UpdateReducerStatesAsync(stoppingToken);
            await Task.Delay(_dbSyncInterval, stoppingToken);
        }
    }

    private async Task StartInitialTipQueryAsync(CancellationToken stoppingToken)
    {
        try
        {
            ICardanoChainProvider chainProvider = chainProviderFactory.CreateProvider();
            Point initialTip = await chainProvider.GetTipAsync();
            EffectiveTipSlot = initialTip.Slot;
            logger.LogInformation("Initial tip established: Slot {InitialTipSlot}", initialTip.Slot);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get initial tip");
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
            var allReducerNames = new HashSet<string>();
            foreach (var reducer in reducers)
            {
                allReducerNames.Add(ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType()));
            }
            
            foreach (string reducerName in allReducerNames)
            {
                // Check if we have recent telemetry data
                var times = _processingTimes.GetValueOrDefault(reducerName, []).ToArray();
                bool hasRecentActivity = times.Length > 0;
                
                if (hasRecentActivity)
                {
                    // Show telemetry-based logs
                    double avgTime = times.Average();
                    ulong latestSlot = _latestSlots.TryGetValue(reducerName, out var slot) ? slot : 0;
                    
                    // Get start slot from reducer state
                    var reducerState = _reducerStates.GetValueOrDefault(reducerName);
                    ulong startSlot = reducerState?.StartIntersection.Slot ?? 0;
                    
                    double progress;
                    if (EffectiveTipSlot <= startSlot)
                    {
                        progress = 100.0;
                    }
                    else
                    {
                        ulong totalSlotsToSync = EffectiveTipSlot - startSlot;
                        ulong totalSlotsSynced = latestSlot >= startSlot ? latestSlot - startSlot : 0;
                        progress = (double)totalSlotsSynced / totalSlotsToSync * 100.0;
                    }
                    
                    logger.LogInformation("[{Reducer}]: {Progress:F1}% - Avg {AvgMs:F1}ms, Slot {Slot}/{EffectiveTip}, Processed {Count}", 
                        reducerName, progress, avgTime, latestSlot, EffectiveTipSlot, times.Length);
                        
                    _processingTimes[reducerName].Clear();
                }
                else
                {
                    // No recent activity, check reducer state from memory or load from database
                    var reducerState = _reducerStates.GetValueOrDefault(reducerName);
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
                            logger.LogWarning(ex, "Failed to load reducer state for {ReducerName}", reducerName);
                            continue;
                        }
                    }
                    
                    if (reducerState != null)
                    {
                        ulong currentSlot = reducerState.LatestIntersections.MaxBy(p => p.Slot)?.Slot ?? reducerState.StartIntersection.Slot;
                        ulong startSlot = reducerState.StartIntersection.Slot;
                        
                        double progress;
                        if (EffectiveTipSlot <= startSlot)
                        {
                            progress = 100.0;
                        }
                        else
                        {
                            ulong totalSlotsToSync = EffectiveTipSlot - startSlot;
                            ulong totalSlotsSynced = currentSlot >= startSlot ? currentSlot - startSlot : 0;
                            progress = (double)totalSlotsSynced / totalSlotsToSync * 100.0;
                        }
                            
                        logger.LogInformation("[{Reducer}]: {Progress:F1}% - Slot {Slot}/{EffectiveTip} (waiting for blocks)", 
                            reducerName, progress, currentSlot, EffectiveTipSlot);
                    }
                }
            }
            
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private void RecordTelemetry(string reducerName, long elapsedMs, ulong slot)
    {
        _processingTimes.AddOrUpdate(reducerName, 
            [elapsedMs], 
            (key, list) => { list.Add(elapsedMs); return list; });
            
        _latestSlots[reducerName] = slot;
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
}