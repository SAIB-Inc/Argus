using Argus.Sync.Data.Models;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Diagnostics;

namespace Argus.Sync.Workers;

public partial class CardanoIndexWorker
{
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
                foreach (IReducer reducer in reducers)
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

                    foreach (IReducer reducer in reducers)
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
                        .AddItem("Current", Math.Round(cpuUsage, 1), CardanoIndexWorker.GetCpuColor(cpuUsage));

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
}
