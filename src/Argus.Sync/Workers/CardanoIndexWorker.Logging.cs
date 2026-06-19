using Argus.Sync.Data.Models;
using Microsoft.Extensions.Logging;

namespace Argus.Sync.Workers;

public partial class CardanoIndexWorker
{
    // --- LoggerMessage source-generated high-performance logging ---

    [LoggerMessage(Level = LogLevel.Information, Message = "Dependency graph built: {RootCount} root reducers, {TotalCount} total reducers")]
    private static partial void LogDependencyGraphBuilt(ILogger logger, int rootCount, int totalCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No root reducers found. All reducers have dependencies, which may indicate a circular dependency.")]
    private static partial void LogNoRootReducers(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting chain sync for {Count} root reducers: {Reducers}")]
    private static partial void LogStartingChainSync(ILogger logger, int count, string reducers);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get the initial intersection for {Reducer}")]
    private static partial void LogFailedInitialIntersection(ILogger logger, string reducer);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting chain sync for {Reducer} with {Count} intersection point(s). LatestSlot={LatestSlot}, OldestSlot={OldestSlot}, SlotPreview=[{SlotPreview}]")]
    private static partial void LogStartingReducerChainSync(ILogger logger, string reducer, int count, string latestSlot, string oldestSlot, string slotPreview);

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

}
