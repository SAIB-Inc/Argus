using System.Collections.Concurrent;
using System.Globalization;
using Argus.Sync.Data.Models;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using NextResponse = Argus.Sync.Data.Models.NextResponse;

namespace Argus.Sync.Workers;

/// <summary>
/// Background service that manages Cardano blockchain synchronization by coordinating
/// multiple reducers, handling block processing, rollbacks, and chain provider connections.
/// </summary>
/// <param name="configuration">Application configuration for sync settings.</param>
/// <param name="logger">Logger instance for diagnostic output.</param>
/// <param name="unitOfWorkFactory">Storage backend: per-block per-branch units of work + reducer-checkpoint reads, supplied by a backend package (e.g. <c>AddCardanoPostgresIndexer</c> from Argus.Sync.EntityFramework, or <c>AddCardanoMongoIndexer</c> from Argus.Sync.MongoDb).</param>
/// <param name="reducers">Collection of registered reducer instances.</param>
/// <param name="chainProviderFactory">Factory for creating chain provider connections.</param>
/// <param name="singleInstanceLock">Optional cross-instance guard; when present, the worker waits for it before processing so only one indexer runs per database. Null disables gating.</param>
public partial class CardanoIndexWorker(
    IConfiguration configuration,
    ILogger<CardanoIndexWorker> logger,
    IBlockUnitOfWorkFactory unitOfWorkFactory,
    IEnumerable<IReducer> reducers,
    IChainProviderFactory chainProviderFactory,
    ISingleInstanceLock? singleInstanceLock = null
) : BackgroundService
{
    private readonly ConcurrentDictionary<string, ReducerState> _reducerStates = [];
    private ulong EffectiveTipSlot;
    private readonly ConcurrentDictionary<string, List<long>> _processingTimes = [];
    private readonly ConcurrentDictionary<string, ulong> _latestSlots = [];

    // TUI update event mechanism
    private readonly SemaphoreSlim _tuiUpdateSignal = new(0, 1);
    private DateTime _lastTuiUpdate = DateTime.MinValue;

    // Dependency graph structures
    private readonly Dictionary<string, IReducer> _reducersByName = [];
    private readonly Dictionary<string, List<string>> _dependentReducers = []; // parent -> dependents
    private readonly Dictionary<string, string?> _reducerDependency = []; // reducer -> its single dependency
    private readonly HashSet<string> _rootReducers = [];
    private readonly ConcurrentDictionary<string, ICardanoChainProvider> _rootReducerProviders = [];

    // Pipeline structures (Commit 3 rearchitecture).
    private readonly Dictionary<string, ReducerGraphProcessor> _graphProcessors = [];
    private readonly List<Task> _pipelineRunTasks = [];

    private readonly int _pipelineChannelCapacity = configuration.GetValue("Sync:Pipeline:ChannelCapacity", 256);
    // Default the commit batch size to 500 (the throughput knee in benchmarks — the
    // per-block fsync is fully amortized by there). Batching only engages while
    // catching up — the drain-at-tip trigger commits per-block once caught up — so
    // steady-state behavior is unchanged. Set Sync:Commit:BatchSize=1 to force strict
    // per-block commits.
    private readonly int _commitBatchSize = Math.Max(1, configuration.GetValue("Sync:Commit:BatchSize", 500));
    private readonly TimeSpan _commitMaxDelay = TimeSpan.FromMilliseconds(Math.Max(1, configuration.GetValue("Sync:Commit:MaxDelayMs", 1000)));

    private readonly long _maxRollbackSlots = configuration.GetValue("CardanoNodeConnection:MaxRollbackSlots", 10_000);
    private readonly int _rollbackBuffer = configuration.GetValue("CardanoNodeConnection:RollbackBuffer", 10);
    private readonly ulong _networkMagic = configuration.GetValue("CardanoNodeConnection:NetworkMagic", 2UL);
    private readonly string _defaultStartHash = configuration.GetValue<string>("CardanoNodeConnection:Hash") ?? throw new InvalidOperationException("Default start hash not configured.");
    private readonly ulong _defaultStartSlot = configuration.GetValue<ulong?>("CardanoNodeConnection:Slot") ?? throw new InvalidOperationException("Default start slot not configured.");
    private readonly bool _rollbackModeEnabled = configuration.GetValue("Sync:Rollback:Enabled", false);
    private readonly bool _exitOnCompletion = configuration.GetValue("Sync:Worker:ExitOnCompletion", true);

    private readonly bool _tuiMode = configuration.GetValue("Sync:Dashboard:TuiMode", true);
    private readonly TimeSpan _dashboardRefreshInterval = TimeSpan.FromMilliseconds(Math.Max(configuration.GetValue("Sync:Dashboard:RefreshInterval", 1000), 2000));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Single-instance gate: park here until this process holds the cross-instance advisory
        // lock, so a redeploy overlap can never run two indexers against one database. Null when
        // the guard is disabled (Sync:SingleInstanceLock:Enabled=false) or in tests that
        // construct the worker directly — in which case there is nothing to wait on.
        if (singleInstanceLock is not null)
        {
            await singleInstanceLock.WaitForAcquisitionAsync(stoppingToken).ConfigureAwait(false);
        }

        // Build dependency graph first
        BuildDependencyGraph();
        BuildGraphProcessors();

        // Initialize state for all reducers (including dependents)
        await InitializeAllReducerStatesAsync(stoppingToken);

        // Worker-owned cancellation linked to the host token. A faulting reducer
        // pipeline cancels this so the chain consumers' EnqueueAsync (which may be
        // parked on a full bounded channel) unwind instead of deadlocking.
        using CancellationTokenSource workerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        // Start each root graph's run loop. They block on their inbox channel.
        foreach (ReducerGraphProcessor processor in _graphProcessors.Values)
        {
            _pipelineRunTasks.Add(processor.RunAsync(workerCts.Token));
        }

        if (!_rollbackModeEnabled)
        {
            // Start TUI dashboard if enabled
            _ = Task.Run(() => InitDashboardAsync(stoppingToken), stoppingToken);

            // Start telemetry aggregation task only if TUI mode is disabled
            if (!_tuiMode)
            {
                _ = Task.Run(() => StartTelemetryAggregationAsync(stoppingToken), stoppingToken);
            }

        }

        // Only start chain sync for root reducers (those with no dependencies)
        List<IReducer> rootReducerInstances = [.. _rootReducers.Select(name => _reducersByName[name])];

        if (rootReducerInstances.Count == 0)
        {
            LogNoRootReducers(logger);
            Exit();
            return;
        }

        string rootReducerNames = string.Join(", ", _rootReducers);
        LogStartingChainSync(logger, rootReducerInstances.Count, rootReducerNames);

        // The worker's run loop: start chain sync for each root, then wait for every
        // chain-sync task AND its pipelines to finish, re-throwing the moment any one
        // faults (WhenAll, fail-fast). Waiting on the pipelines — not just the chain-sync
        // tasks — is what both drains in-flight work before exit and stops a faulted
        // reducer from deadlocking on backpressure; surfacing their faults is what stops a
        // chain-consumer error from being masked as exit 0. Returns only on graceful
        // completion of all roots, the first fault, or host shutdown.
        List<Task> running =
        [
            .. rootReducerInstances.Select(reducer => StartReducerChainSyncAsync(reducer, workerCts.Token)),
            .. _pipelineRunTasks,
        ];

        while (running.Count > 0)
        {
            Task completed = await Task.WhenAny(running);
            _ = running.Remove(completed);

            if (completed.IsFaulted)
            {
                // Cancel so the other consumers' parked EnqueueAsync unwind, then re-throw
                // out of ExecuteAsync so the host sees a real failure (not a masked exit 0).
                if (!workerCts.IsCancellationRequested)
                {
                    await workerCts.CancelAsync();
                }

                await completed; // re-throws the original exception
            }
        }

        Exit();
    }

    private async Task StartReducerChainSyncAsync(IReducer reducer, CancellationToken stoppingToken)
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

            // Rollback mode: a one-shot operational mode that, instead of resuming from the saved checkpoint,
            // rewinds the chain to an operator-configured intersection. The whole feature lives under a single
            // Sync:Rollback:* namespace — the master switch (Sync:Rollback:Enabled), the global target
            // (Sync:Rollback:Hash / :Slot), and optional per-reducer overrides
            // (Sync:Rollback:Reducers:{name}:Enabled / :Hash / :Slot).
            if (_rollbackModeEnabled)
            {
                rollbackMode = configuration.GetValue($"Sync:Rollback:Reducers:{reducerName}:Enabled", true);

                if (rollbackMode)
                {
                    string? defaultRollbackHash = configuration.GetValue<string>("Sync:Rollback:Hash");
                    ulong defaultRollbackSlot = configuration.GetValue<ulong>("Sync:Rollback:Slot");
                    string? selfRollbackHash = configuration.GetValue<string>($"Sync:Rollback:Reducers:{reducerName}:Hash");
                    ulong selfRollbackSlot = configuration.GetValue<ulong>($"Sync:Rollback:Reducers:{reducerName}:Slot");
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
            (string latestSlot, string oldestSlot, string slotPreview) = SummarizeIntersections(intersectionList);
            LogStartingReducerChainSync(logger, reducerName, intersectionList.Count, latestSlot, oldestSlot, slotPreview);

            string rootReducerName = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
            ReducerGraphProcessor rootProcessor = _graphProcessors[rootReducerName];

            try
            {
                await foreach (NextResponse nextResponse in chainProvider.StartChainSyncAsync(intersectionList, _networkMagic, stoppingToken))
                {
                    if (nextResponse.Action == NextResponseAction.Await)
                    {
                        throw new NotImplementedException("NextResponseAction.Await is not supported by the channel pipeline.");
                    }

                    // Rollback safety check — fail fast if a deep rollback hits
                    // the configured maxRollbackSlots ceiling. Doing this at the
                    // chain consumer (not the pipeline) means we stop pulling
                    // from the node before pushing into a doomed channel.
                    if (nextResponse.Action == NextResponseAction.RollBack && !rollbackMode)
                    {
                        ulong rbSlot = nextResponse.RollBackType switch
                        {
                            RollBackType.Exclusive => nextResponse.RollbackSlot!.Value + 1UL,
                            RollBackType.Inclusive => nextResponse.RollbackSlot!.Value,
                            _ => 0
                        };
                        long rbDepth = (long)_reducerStates[rootReducerName].LatestSlot - (long)rbSlot;
                        if (rbDepth >= _maxRollbackSlots)
                        {
                            throw new InvalidOperationException(
                                $"Requested RollBack Slot {rbSlot} is more than {_maxRollbackSlots} slots behind current slot {_reducerStates[rootReducerName].LatestSlot}.");
                        }
                    }

                    // Pipeline ownership: BranchUow=null tells the root pipeline
                    // it's a branch root (creates its own UoW). The chain consumer
                    // doesn't wait on the dep tree — control returns immediately
                    // to pull the next block, suspending only when the root
                    // pipeline's bounded channel is full (cooperative backpressure).
                    await rootProcessor.EnqueueAsync(nextResponse, stoppingToken).ConfigureAwait(false);

                    if (rollbackMode)
                    {
                        LogRollbackCompleted(logger);
                        Exit();
                        return;
                    }
                }
            }
            finally
            {
                // Signal end-of-stream to this root pipeline. Completion cascades
                // downstream: each dependent expects one vote per upstream
                // producer, so once the parent's RunAsync drains its inbox and
                // calls Complete on its dependents, the cascade unwinds cleanly.
                rootProcessor.Complete();
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

    private static (string LatestSlot, string OldestSlot, string SlotPreview) SummarizeIntersections(List<Point> intersections)
    {
        if (intersections.Count == 0)
        {
            return ("none", "none", "none");
        }

        List<ulong> orderedSlots = [.. intersections
            .OrderByDescending(p => p.Slot)
            .Select(p => p.Slot)];
        string preview = string.Join(", ", orderedSlots.Take(5));
        if (orderedSlots.Count > 5)
        {
            preview = $"{preview}, ...";
        }

        return (
            orderedSlots[0].ToString(CultureInfo.InvariantCulture),
            orderedSlots[^1].ToString(CultureInfo.InvariantCulture),
            preview);
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
