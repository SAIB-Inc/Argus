using System.Diagnostics;
using System.Threading.Channels;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Microsoft.Extensions.Logging;
using IBlock = Chrysalis.Codec.Types.Cardano.Core.IBlock;

namespace Argus.Sync.Workers;

/// <summary>
/// Processes one root's entire dependency subgraph as a single sequential branch over
/// one batched unit of work. A block is run through every reducer in topological order
/// (parents before children) into the <em>same</em> <see cref="IBlockUnitOfWork"/> — so a
/// child sees its parent's writes via the shared change-tracker (<c>ctx.X.Local</c>) or a
/// DB query within the open transaction, exactly as before — and the whole graph's writes
/// plus every reducer's checkpoint commit together when a batch trigger fires
/// (<c>Sync:Commit:BatchSize</c>, <c>Sync:Commit:MaxDelayMs</c>, or the inbox draining at
/// tip). This replaces the per-reducer pipeline + fork model: there is no per-branch UoW
/// forwarding and no fork commit-then-spawn — independent siblings simply run back-to-back
/// in the same pass. One fsync per batch covers the entire graph, and because the graph
/// commits atomically every reducer shares one checkpoint (recovery resumes from a single
/// point; replay is idempotent for ≤ BatchSize blocks).
///
/// Block DB work runs on <see cref="CancellationToken.None"/> so shutdown never interrupts
/// a write mid-flight; cancellation is observed only between blocks at the inbox read, and
/// the final partial batch commits in the run loop's finally.
/// </summary>
internal sealed partial class ReducerGraphProcessor
{
    private readonly IReadOnlyList<IReducer> _reducers; // topological order, parents first
    private readonly IReadOnlyList<string> _names;      // parallel to _reducers
    private readonly IBlockUnitOfWorkFactory _uowFactory;
    private readonly Channel<NextResponse> _inbox;
    private readonly ILogger _logger;
    private readonly Action<string, long, ulong>? _telemetryRecorder;
    private readonly Action<string, Point>? _intersectionRecorder;
    private readonly Action<string, ulong>? _rollbackRecorder;

    private readonly int _batchSize;
    private readonly TimeSpan _maxBatchDelay;
    private IBlockUnitOfWork? _batchUow;
    private int _batchCount;
    private long _batchStartTimestamp;

    /// <summary>Intersections deferred across no-op blocks (carried to the next data-bearing commit).</summary>
    private readonly Dictionary<string, Point> _pendingDeferred = [];

    /// <summary>The most recent slot processed (telemetry only).</summary>
    public ulong LatestSlot { get; private set; }

    public ReducerGraphProcessor(
        IReadOnlyList<IReducer> topologicallyOrderedReducers,
        IBlockUnitOfWorkFactory uowFactory,
        int channelCapacity,
        int batchSize,
        TimeSpan maxBatchDelay,
        ILogger logger,
        Action<string, long, ulong>? telemetryRecorder = null,
        Action<string, Point>? intersectionRecorder = null,
        Action<string, ulong>? rollbackRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(topologicallyOrderedReducers);
        ArgumentNullException.ThrowIfNull(uowFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _reducers = topologicallyOrderedReducers;
        _names = [.. _reducers.Select(r => ArgusUtil.GetTypeNameWithoutGenerics(r.GetType()))];
        _uowFactory = uowFactory;
        _batchSize = Math.Max(1, batchSize);
        _maxBatchDelay = maxBatchDelay;
        _logger = logger;
        _telemetryRecorder = telemetryRecorder;
        _intersectionRecorder = intersectionRecorder;
        _rollbackRecorder = rollbackRecorder;
        _inbox = Channel.CreateBounded<NextResponse>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });
    }

    /// <summary>Pushes a chain response into the processor's inbox. Suspends if full (backpressure).</summary>
    public ValueTask EnqueueAsync(NextResponse response, CancellationToken ct) => _inbox.Writer.WriteAsync(response, ct);

    /// <summary>Signals end-of-stream; the run loop drains and exits.</summary>
    public void Complete() => _inbox.Writer.TryComplete();

    /// <summary>Run loop: consume chain responses, batching forward blocks and committing on triggers.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (NextResponse response in _inbox.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    if (response.Action == NextResponseAction.RollForward)
                    {
                        await ProcessRollForwardAsync(response).ConfigureAwait(false);
                    }
                    else
                    {
                        // A rollback ends the batch: commit what we've accumulated (so a
                        // rollback inside the batch keeps valid pre-fork blocks), then apply
                        // the rewind across the whole graph in its own immediate commit.
                        await CommitBatchAsync().ConfigureAwait(false);
                        await ProcessRollBackAsync(response).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    LogGraphError(_logger, ex);
                    await DiscardOpenBatchAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown between blocks — fall through to commit the final batch.
        }
        finally
        {
            // Persist the final partial batch (best-effort; a hard abort that loses it is
            // recovered by idempotent replay from the last committed checkpoint).
            try { await CommitBatchAsync().ConfigureAwait(false); }
            catch (Exception ex) { LogGraphError(_logger, ex); }
        }
    }

    private async Task ProcessRollForwardAsync(NextResponse response)
    {
        if (_batchUow is null)
        {
            _batchUow = await _uowFactory.CreateAsync(CancellationToken.None).ConfigureAwait(false);
            _batchCount = 0;
            _batchStartTimestamp = Stopwatch.GetTimestamp();
        }

        IBlock block = response.Block!;
        ulong slot = block.Header().HeaderBody().Slot();
        Point point = new(block.Header().Hash(), slot);

        // One sequential pass through the graph; every reducer writes the same UoW, so a
        // child reads its parent's writes via .Local / a same-transaction query.
        for (int i = 0; i < _reducers.Count; i++)
        {
            string name = _names[i];
            Stopwatch sw = Stopwatch.StartNew();
            await _reducers[i].RollForwardAsync(block, _batchUow, CancellationToken.None).ConfigureAwait(false);
            sw.Stop();
            _batchUow.TrackIntersection(name, point);
            _intersectionRecorder?.Invoke(name, point);
            _telemetryRecorder?.Invoke(name, sw.ElapsedMilliseconds, slot);
        }
        LatestSlot = slot;

        // Flush (no commit/fsync) so this block is visible to later blocks in the batch.
        await _batchUow.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        _batchCount++;

        bool full = _batchCount >= _batchSize;
        bool delayElapsed = _maxBatchDelay > TimeSpan.Zero
            && Stopwatch.GetElapsedTime(_batchStartTimestamp) >= _maxBatchDelay;
        bool drained = _inbox.Reader.Count == 0;
        if (full || delayElapsed || drained)
        {
            await CommitBatchAsync().ConfigureAwait(false);
        }
    }

    private async Task ProcessRollBackAsync(NextResponse response)
    {
        ulong rollbackSlot = response.RollBackType switch
        {
            RollBackType.Exclusive => response.RollbackSlot!.Value + 1UL,
            RollBackType.Inclusive => response.RollbackSlot!.Value,
            _ => 0
        };

        IBlockUnitOfWork uow = await _uowFactory.CreateAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            for (int i = 0; i < _reducers.Count; i++)
            {
                string name = _names[i];
                await _reducers[i].RollBackwardAsync(rollbackSlot, uow, CancellationToken.None).ConfigureAwait(false);
                uow.TrackRollback(name, rollbackSlot);
                _rollbackRecorder?.Invoke(name, rollbackSlot);
            }
            LatestSlot = response.RollbackSlot!.Value;
            // Rollbacks commit immediately and never defer (the rewind must persist).
            _pendingDeferred.Clear();
            _ = await uow.CommitAsync(deferIfEmpty: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            try { await uow.RollbackAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* secondary */ }
            throw;
        }
        finally
        {
            await uow.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Commits the open batch (an all-empty batch defers its checkpoints to the next commit). No-op if none open.</summary>
    private async Task CommitBatchAsync()
    {
        if (_batchUow is null)
        {
            return;
        }
        IBlockUnitOfWork uow = _batchUow;
        _batchUow = null;
        _batchCount = 0;

        // Carry over intersections deferred from earlier no-op blocks; current points win.
        foreach ((string name, Point point) in _pendingDeferred)
        {
            if (!uow.TrackedIntersections.TryGetValue(name, out Point? current) || current.Slot < point.Slot)
            {
                uow.TrackIntersection(name, point);
            }
        }

        Dictionary<string, Point> snapshot = new(uow.TrackedIntersections);
        bool committed;
        try
        {
            committed = await uow.CommitAsync(deferIfEmpty: true, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await uow.DisposeAsync().ConfigureAwait(false);
        }

        _pendingDeferred.Clear();
        if (!committed)
        {
            foreach ((string name, Point point) in snapshot)
            {
                _pendingDeferred[name] = point;
            }
        }
    }

    /// <summary>Rolls back and disposes the open batch (error/shutdown cleanup). No-op if none open.</summary>
    private async Task DiscardOpenBatchAsync()
    {
        if (_batchUow is null)
        {
            return;
        }
        IBlockUnitOfWork uow = _batchUow;
        _batchUow = null;
        _batchCount = 0;
        try { await uow.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { LogGraphError(_logger, ex); }
        await uow.DisposeAsync().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reducer graph processor failed")]
    private static partial void LogGraphError(ILogger logger, Exception ex);
}
