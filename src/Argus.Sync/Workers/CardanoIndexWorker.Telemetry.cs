using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;

namespace Argus.Sync.Workers;

public partial class CardanoIndexWorker
{
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
            foreach (IReducer reducer in reducers)
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
}
