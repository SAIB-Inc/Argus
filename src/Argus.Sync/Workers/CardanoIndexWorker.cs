using System.Collections.Concurrent;
using System.Text.Json;
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
using Argus.Sync.Extensions;
using System.Diagnostics;

namespace Argus.Sync.Workers;

public class CardanoIndexWorker<T>(
    IConfiguration configuration,
    ILogger<CardanoIndexWorker<T>> logger,
    IDbContextFactory<T> dbContextFactory,
    IEnumerable<IReducer<IReducerModel>> reducers
) : BackgroundService where T : CardanoDbContext
{
    private readonly ConcurrentDictionary<string, ReducerState> _reducerStates = [];
    private Point CurrentTip = new(string.Empty, 0);

    private readonly long _maxRollbackSlots = configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000);
    private readonly int _rollbackBuffer = configuration.GetValue("CardanoNodeConnection:RollbackBuffer", 10);
    private readonly ulong _networkMagic = configuration.GetValue("CardanoNodeConnection:NetworkMagic", 2UL);
    private readonly string _connectionType = configuration.GetValue<string>("CardanoNodeConnection:ConnectionType") ?? throw new Exception("Connection type not configured.");
    private readonly string? _socketPath = configuration.GetValue<string?>("CardanoNodeConnection:UnixSocket:Path");
    private readonly string? _gRPCEndpoint = configuration.GetValue<string?>("CardanoNodeConnection:gRPC:Endpoint");
    private readonly string? _apiKey = configuration.GetValue<string?>("CardanoNodeConnection:gRPC:ApiKey");
    private readonly string _defaultStartHash = configuration.GetValue<string>("CardanoNodeConnection:Hash") ?? throw new Exception("Default start hash not configured.");
    private readonly ulong _defaultStartSlot = configuration.GetValue<ulong?>("CardanoNodeConnection:Slot") ?? throw new Exception("Default start slot not configured.");
    private readonly bool _rollbackModeEnabled = configuration.GetValue("Sync:Rollback:Enabled", false);

    private readonly bool _tuiMode = configuration.GetValue("Sync:Dashboard:TuiMode", true);
    private readonly PeriodicTimer _dashboardTimer = new(TimeSpan.FromMilliseconds(Math.Max(configuration.GetValue("Sync:Dashboard:RefreshInterval", 1000), 2000)));
    private readonly PeriodicTimer _dbSyncTimer = new(TimeSpan.FromMilliseconds(configuration.GetValue("Sync:State:ReducerStateSyncInterval", 10000)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_rollbackModeEnabled)
        {
            _ = Task.Run(InitDashboardAsync, stoppingToken);
            _ = Task.Run(async () => await StartReducerStateSync(stoppingToken), stoppingToken);
        }

        await Task.WhenAny(reducers.Select(reducer => StartReducerChainSyncAsync(reducer, stoppingToken)));

        Environment.Exit(0);
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

            ICardanoChainProvider chainProvider = GetCardanoChainProvider();
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
                    Environment.Exit(0);
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
        await reducer.RollForwardAsync(response.Block);

        Point recentIntersection = new(response.Block.Header().Hash(), response.Block.Header().HeaderBody().Slot());
        IEnumerable<Point> latestIntersections = UpdateLatestIntersections(_reducerStates[reducerName].LatestIntersections, recentIntersection);
        _reducerStates[reducerName] = _reducerStates[reducerName] with
        {
            LatestIntersections = latestIntersections
        };
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

        await reducer.RollBackwardAsync(rollbackSlot);

        IEnumerable<Point> latestIntersections = _reducerStates[reducerName].LatestIntersections;
        latestIntersections = latestIntersections.Where(i => i.Slot < rollbackSlot);
        _reducerStates[reducerName] = _reducerStates[reducerName] with
        {
            LatestIntersections = latestIntersections
        };
    }

    private ICardanoChainProvider GetCardanoChainProvider() => _connectionType switch
    {
        "UnixSocket" => new N2CProvider(_socketPath ?? throw new InvalidOperationException("Socket path is not configured.")),
        "TCP" => throw new NotImplementedException("TCP connection type is not yet implemented."),
        "gRPC" => new U5CProvider(
            _gRPCEndpoint ?? throw new Exception("gRPC endpoint is not configured."),
            new Dictionary<string, string>
            {
                { "dmtr-api-key", _apiKey ?? throw new Exception("Demeter API key is missing") }
            }
        ),
        _ => throw new Exception("Invalid chain provider")
    };

    private async Task<ReducerState> GetReducerStateAsync(string reducerName, CancellationToken stoppingToken)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync(stoppingToken);

        ReducerState? state = await dbContext.ReducerStates
            .AsNoTracking()
            .Where(state => state.Name == reducerName)
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

    private async Task InitDashboardAsync()
    {
        if (_tuiMode)
        {
            await Task.Delay(500);
            if (configuration.GetValue<string>("Sync:Dashboard:DisplayType") == "Full")
            {
                _ = Task.Run(StartSyncFullDashboardTracker);
            }
            else
            {
                _ = Task.Run(StartSyncProgressTrackerAsync);
            }
        }
        else
        {
            _ = Task.Run(StartSyncProgressPlainTextTracker);
        }

        await Task.CompletedTask;
    }

    private async Task StartSyncProgressTrackerAsync()
    {
        await Task.Delay(100);
        ICardanoChainProvider chainProvider = GetCardanoChainProvider();

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

                while (await _dashboardTimer.WaitForNextTickAsync())
                {
                    CurrentTip = await chainProvider.GetTipAsync();

                    foreach (ProgressTask task in tasks)
                    {
                        ReducerState state = _reducerStates[task.Description];
                        ulong startSlot = state.StartIntersection.Slot;
                        ulong currentSlot = state.LatestIntersections.MaxBy(p => p.Slot)?.Slot ?? 0UL;
                        ulong tipSlot = CurrentTip.Slot;

                        if (tipSlot <= startSlot)
                        {
                            task.Value = 100.0;
                        }
                        else
                        {
                            ulong totalSlotsToSync = tipSlot - startSlot;
                            ulong totalSlotsSynced = currentSlot >= startSlot ? currentSlot - startSlot : 0;
                            double progress = (double)totalSlotsSynced / totalSlotsToSync * 100.0;
                            task.Value = (double)totalSlotsSynced / totalSlotsToSync * 100.0;
                        }
                    }
                }
            }
        );
    }

    private async Task StartSyncProgressPlainTextTracker()
    {
        await Task.Delay(100);
        ICardanoChainProvider chainProvider = GetCardanoChainProvider();

        while (await _dashboardTimer.WaitForNextTickAsync())
        {
            CurrentTip = await chainProvider.GetTipAsync();

            foreach (ReducerState state in _reducerStates.Values)
            {
                Point? currentIntersection = state.LatestIntersections.MaxBy(p => p.Slot);
                ulong startSlot = state.StartIntersection.Slot;
                ulong currentSlot = currentIntersection?.Slot ?? 0UL;
                ulong tipSlot = CurrentTip.Slot;

                ulong totalSlotsToSync = tipSlot - startSlot;
                ulong totalSlotsSynced = currentSlot >= startSlot ? currentSlot - startSlot : 0;
                double progress = tipSlot <= startSlot ? 100.0 : (double)totalSlotsSynced / totalSlotsToSync * 100.0;

                var status = new
                {
                    ReducerName = state.Name,
                    state.StartIntersection,
                    CurrentIntersection = currentIntersection,
                    SyncProgress = progress
                };

                string statusJson = JsonSerializer.Serialize(status);

                logger.LogInformation("[{reducer}]: {status}", state.Name, statusJson);
            }

            Process processInfo = Process.GetCurrentProcess();
            processInfo.Refresh();

            DateTime lastCpuCheck = DateTime.Now;
            TimeSpan lastCpuTotal = processInfo.TotalProcessorTime;

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

            var resourceStats = new
            {
                Memory = new
                {
                    MemoryUsedMB = memoryUsedMB,
                    PrivateMemoryMB = privateMemoryMB,
                    ManagedMemoryMB = managedMemoryMB
                },
                CPU = new
                {
                    Usage = cpuUsage,
                    ThreadCount = processInfo.Threads.Count,
                    processInfo.HandleCount
                }
            };

            string resourceStatsJson = JsonSerializer.Serialize(resourceStats);
            logger.LogInformation("[Resource]: {stats}", resourceStatsJson);
        }
    }

    private async Task StartSyncFullDashboardTracker()
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
                ICardanoChainProvider chainProvider = GetCardanoChainProvider();
                Process processInfo = Process.GetCurrentProcess();

                // Sync Progress
                while (await _dashboardTimer.WaitForNextTickAsync())
                {
                    CurrentTip = await chainProvider.GetTipAsync();

                    BarChart syncBarChart = new BarChart()
                        .WithMaxValue(100.0)
                        .Label("[darkorange3 bold underline]Syncing...[/]")
                        .LeftAlignLabel();

                    double overallProgress = 0.0;
                    foreach (ReducerState state in _reducerStates.Values)
                    {
                        Point? currentIntersection = state.LatestIntersections.MaxBy(p => p.Slot);
                        ulong startSlot = state.StartIntersection.Slot;
                        ulong currentSlot = currentIntersection?.Slot ?? 0UL;
                        ulong tipSlot = CurrentTip.Slot;

                        ulong totalSlotsToSync = tipSlot - startSlot;
                        ulong totalSlotsSynced = currentSlot >= startSlot ? currentSlot - startSlot : 0;
                        double progress = tipSlot <= startSlot ? 100.0 : (double)totalSlotsSynced / totalSlotsToSync * 100.0;

                        syncBarChart.AddItem(state.Name, Math.Round(progress, 2), GetProgressColor(progress));

                        overallProgress += progress;
                    }

                    // Overall Progress
                    overallProgress /= _reducerStates.Values.Count;
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
        while (!stoppingToken.IsCancellationRequested && await _dbSyncTimer.WaitForNextTickAsync(stoppingToken))
        {
            await UpdateReducerStatesAsync(stoppingToken);
        }
    }
}