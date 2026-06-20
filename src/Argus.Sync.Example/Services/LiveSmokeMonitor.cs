using Argus.Sync.Data.Models;
using Argus.Sync.Example.Data;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Services;

/// <summary>
/// Example-only live smoke monitor. It observes the real Postgres tables while
/// the indexer talks to a real node and turns the example into a bounded,
/// scriptable smoke run.
/// </summary>
public sealed partial class LiveSmokeMonitor(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ILogger<LiveSmokeMonitor> logger) : BackgroundService
{
    private static readonly string[] DefaultRequiredReducers =
    [
        "BlockTestReducer",
        "DependentTransactionReducer",
        "ChainedDependentReducer",
        "TransactionTestReducer",
    ];

    private readonly bool _enabled = configuration.GetValue("Example:Smoke:Enabled", false);
    private readonly int _stopAfterBlocks = Math.Max(0, configuration.GetValue("Example:Smoke:StopAfterBlocks", 500));
    private readonly int _stopAfterSeconds = Math.Max(0, configuration.GetValue("Example:Smoke:StopAfterSeconds", 180));
    private readonly TimeSpan _stopAfter = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue("Example:Smoke:StopAfterSeconds", 180)));
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Example:Smoke:PollIntervalSeconds", 5)));
    private readonly TimeSpan _failIfNoProgress = TimeSpan.FromSeconds(Math.Max(0, configuration.GetValue("Example:Smoke:FailIfNoProgressSeconds", 60)));
    private readonly bool _requireTransactionProgress = configuration.GetValue("Example:Smoke:RequireTransactionProgress", true);
    private readonly string[] _requiredReducers = ReadRequiredReducers(configuration);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        TestDbContext dbContext = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        SmokeSnapshot baseline = await ReadSnapshotAsync(dbContext, stoppingToken).ConfigureAwait(false);
        SmokeSnapshot previous = baseline;
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset lastProgressAt = startedAt;

        if (logger.IsEnabled(LogLevel.Information))
        {
            LogStarted(
                logger,
                baseline.BlockCount,
            baseline.TransactionCount,
            _stopAfterBlocks,
            _stopAfterSeconds,
            _requireTransactionProgress);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, stoppingToken).ConfigureAwait(false);

            SmokeSnapshot current = await ReadSnapshotAsync(dbContext, stoppingToken).ConfigureAwait(false);
            if (current.HasProgressSince(previous))
            {
                lastProgressAt = DateTimeOffset.UtcNow;
            }

            LogSnapshot(baseline, current);

            long addedBlocks = current.BlockCount - baseline.BlockCount;
            bool blockTargetReached = _stopAfterBlocks > 0 && addedBlocks >= _stopAfterBlocks;
            bool requirementsMet = RequirementsMet(baseline, current, out string missingRequirements);
            if (blockTargetReached && requirementsMet)
            {
                LogBlockTargetReached(logger, addedBlocks);
                lifetime.StopApplication();
                return;
            }

            if (blockTargetReached)
            {
                LogWaitingForRequirements(logger, missingRequirements);
            }

            TimeSpan elapsed = DateTimeOffset.UtcNow - startedAt;
            if (_stopAfter > TimeSpan.Zero && elapsed >= _stopAfter)
            {
                if (requirementsMet)
                {
                    LogDurationReached(logger, elapsed.TotalSeconds);
                    lifetime.StopApplication();
                }
                else
                {
                    LogDurationReachedWithoutRequirements(logger, elapsed.TotalSeconds, missingRequirements);
                    Environment.ExitCode = 1;
                    lifetime.StopApplication();
                }

                return;
            }

            TimeSpan idle = DateTimeOffset.UtcNow - lastProgressAt;
            if (_failIfNoProgress > TimeSpan.Zero && idle >= _failIfNoProgress)
            {
                LogNoProgress(logger, idle.TotalSeconds);
                Environment.ExitCode = 1;
                lifetime.StopApplication();
                return;
            }

            previous = current;
        }
    }

    private static string[] ReadRequiredReducers(IConfiguration configuration)
    {
        string[] configured = configuration
            .GetSection("Example:Smoke:RequiredReducers")
            .Get<string[]>() ?? [];

        return configured.Length == 0 ? DefaultRequiredReducers : configured;
    }

    private static async Task<SmokeSnapshot> ReadSnapshotAsync(TestDbContext dbContext, CancellationToken ct)
    {
        dbContext.ChangeTracker.Clear();

        long blockCount = await dbContext.BlockTests.LongCountAsync(ct).ConfigureAwait(false);
        long transactionCount = await dbContext.TransactionTests.LongCountAsync(ct).ConfigureAwait(false);
        ulong latestBlockSlot = await dbContext.BlockTests
            .Select(b => (ulong?)b.Slot)
            .MaxAsync(ct).ConfigureAwait(false) ?? 0UL;

        List<ReducerState> reducerStates = await dbContext.ReducerStates
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<string, ulong> reducerSlots = reducerStates.ToDictionary(
            r => r.Name,
            r => r.LatestIntersections.MaxBy(p => p.Slot)?.Slot ?? 0UL);

        return new SmokeSnapshot(blockCount, transactionCount, latestBlockSlot, reducerSlots);
    }

    private bool RequirementsMet(SmokeSnapshot baseline, SmokeSnapshot current, out string missingRequirements)
    {
        List<string> missing = [];

        if (current.BlockCount <= baseline.BlockCount)
        {
            missing.Add("BlockTests rows");
        }

        if (_requireTransactionProgress && current.TransactionCount <= baseline.TransactionCount)
        {
            missing.Add("TransactionTests rows");
        }

        foreach (string reducer in _requiredReducers)
        {
            if (current.ReducerSlot(reducer) <= baseline.ReducerSlot(reducer))
            {
                missing.Add($"{reducer} state");
            }
        }

        missingRequirements = string.Join(", ", missing);
        return missing.Count == 0;
    }

    private void LogSnapshot(SmokeSnapshot baseline, SmokeSnapshot current)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        long addedBlocks = current.BlockCount - baseline.BlockCount;
        long addedTransactions = current.TransactionCount - baseline.TransactionCount;
        string reducerProgress = current.ReducerSlots.Count == 0
            ? "none"
            : string.Join(", ", current.ReducerSlots.Select(kvp => $"{kvp.Key}:{kvp.Value}"));

        LogProgress(logger, addedBlocks, addedTransactions, current.LatestBlockSlot, reducerProgress);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Live smoke monitor started: baselineBlocks={BaselineBlocks}, baselineTxs={BaselineTransactions}, stopAfterBlocks={StopAfterBlocks}, stopAfterSeconds={StopAfterSeconds}, requireTransactionProgress={RequireTransactionProgress}")]
    private static partial void LogStarted(ILogger logger, long baselineBlocks, long baselineTransactions, int stopAfterBlocks, int stopAfterSeconds, bool requireTransactionProgress);

    [LoggerMessage(Level = LogLevel.Information, Message = "Live smoke target reached: addedBlocks={AddedBlocks}")]
    private static partial void LogBlockTargetReached(ILogger logger, long addedBlocks);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live smoke block target reached, waiting for required progress: {MissingRequirements}")]
    private static partial void LogWaitingForRequirements(ILogger logger, string missingRequirements);

    [LoggerMessage(Level = LogLevel.Information, Message = "Live smoke duration reached: elapsedSeconds={ElapsedSeconds:N0}")]
    private static partial void LogDurationReached(ILogger logger, double elapsedSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Live smoke duration reached before required progress: elapsedSeconds={ElapsedSeconds:N0}, missing=[{MissingRequirements}]")]
    private static partial void LogDurationReachedWithoutRequirements(ILogger logger, double elapsedSeconds, string missingRequirements);

    [LoggerMessage(Level = LogLevel.Error, Message = "Live smoke made no DB progress for {IdleSeconds:N0}s")]
    private static partial void LogNoProgress(ILogger logger, double idleSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Live smoke progress: addedBlocks={AddedBlocks}, addedTxs={AddedTransactions}, latestBlockSlot={LatestBlockSlot}, reducers=[{ReducerProgress}]")]
    private static partial void LogProgress(ILogger logger, long addedBlocks, long addedTransactions, ulong latestBlockSlot, string reducerProgress);

    private sealed record SmokeSnapshot(
        long BlockCount,
        long TransactionCount,
        ulong LatestBlockSlot,
        IReadOnlyDictionary<string, ulong> ReducerSlots)
    {
        public ulong ReducerSlot(string reducerName)
            => ReducerSlots.TryGetValue(reducerName, out ulong slot) ? slot : 0UL;

        public bool HasProgressSince(SmokeSnapshot previous)
            => BlockCount > previous.BlockCount
            || TransactionCount > previous.TransactionCount
            || LatestBlockSlot > previous.LatestBlockSlot
            || ReducerSlots.Any(kvp =>
                !previous.ReducerSlots.TryGetValue(kvp.Key, out ulong priorSlot)
                || kvp.Value > priorSlot);
    }
}
