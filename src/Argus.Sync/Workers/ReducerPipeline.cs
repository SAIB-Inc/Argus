using System.Diagnostics;
using System.Threading.Channels;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Microsoft.Extensions.Logging;

namespace Argus.Sync.Workers;

/// <summary>
/// Per-reducer pipeline: a bounded <see cref="Channel{T}"/> inbox + a run-loop
/// task that pulls envelopes, processes them through the reducer, and forwards
/// to direct dependents. Pipelines are wired into a graph that mirrors the
/// reducers' <c>[DependsOn]</c> declarations.
///
/// **Per-branch UoW lifecycle**: a "branch" is a maximal linear chain
/// `A → B → C ...` whose every interior node has exactly one dependent. The
/// branch root (chain root, or any direct child of a fork point) creates a
/// fresh <see cref="IBlockUnitOfWork"/>; intermediate nodes forward the same
/// UoW to their single dependent; the branch leaf commits the UoW. At a fork
/// point (>1 dependents), the current pipeline commits its UoW first, then
/// spawns fresh UoWs for each child-branch root.
///
/// **Bounded backpressure**: channels are constructed with
/// <see cref="BoundedChannelFullMode.Wait"/>. When a downstream pipeline is
/// slow, the producer's <c>WriteAsync</c> suspends; the suspension propagates
/// upstream until the chain consumer stops pulling from the node. Memory is
/// bounded by `Σ (channel_capacity × envelope_size)` across all pipelines.
///
/// **Per-branch atomicity (NOT per-block across a fork)**: each branch commits in its own
/// transaction — the branch leaf, or the fork node before it spawns children. A fork is therefore
/// not atomic across its children: if one child faults, the worker fails fast, but a sibling (or the
/// parent) that already committed is <em>not</em> rolled back. That is by design — independent
/// branches commit independently so they can run in parallel. Recovery is by restart + replay from
/// the last committed checkpoint, which is safe because each commit persists that reducer's
/// checkpoint atomically with its data; replay re-derives the failed branch's block.
/// </summary>
internal sealed partial class ReducerPipeline
{
    private readonly IReducer _reducer;
    private readonly IBlockUnitOfWorkFactory _uowFactory;
    private readonly Channel<Envelope> _inbox;
    private readonly List<ReducerPipeline> _dependents = [];
    private readonly ILogger _logger;
    private readonly Action<string, long, ulong>? _telemetryRecorder;
    private readonly Action<string, Point>? _intersectionRecorder;
    private readonly Action<string, ulong>? _rollbackRecorder;

    // Batch-commit (independent / 0-dependent reducers only): the open batch UoW is
    // reused across up to _batchSize blocks — or until _maxBatchDelay elapses, or the
    // inbox drains at tip — so the durable commit/fsync amortizes across the batch.
    // _batchSize == 1 reproduces per-block commit exactly.
    private readonly int _batchSize;
    private readonly TimeSpan _maxBatchDelay;
    private IBlockUnitOfWork? _batchUow;
    private int _batchCount;
    private long _batchStartTimestamp;

    private int _completionVotes;
    private int _expectedCompletionVotes;

    /// <summary>
    /// Buffer of intersections deferred across no-op blocks. Only populated
    /// when this pipeline commits (leaf or fork point). When a UoW commits no
    /// data, the framework stashes its tracked intersections here and applies
    /// them to the next data-bearing UoW's commit. Crash-safe: deferred state
    /// only covers blocks where no reducer wrote data, so replay is a no-op.
    /// </summary>
    private readonly Dictionary<string, Point> _pendingDeferred = [];

    /// <summary>The reducer's logical name (type name without generic arity).</summary>
    public string Name { get; }

    /// <summary>The most recent slot processed by this pipeline (telemetry only).</summary>
    public ulong LatestSlot { get; private set; }

    public ReducerPipeline(
        IReducer reducer,
        IBlockUnitOfWorkFactory uowFactory,
        int channelCapacity,
        ILogger logger,
        Action<string, long, ulong>? telemetryRecorder = null,
        Action<string, Point>? intersectionRecorder = null,
        Action<string, ulong>? rollbackRecorder = null,
        int batchSize = 1,
        TimeSpan maxBatchDelay = default)
    {
        ArgumentNullException.ThrowIfNull(reducer);
        ArgumentNullException.ThrowIfNull(uowFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _reducer = reducer;
        _uowFactory = uowFactory;
        _logger = logger;
        _telemetryRecorder = telemetryRecorder;
        _intersectionRecorder = intersectionRecorder;
        _rollbackRecorder = rollbackRecorder;
        _batchSize = Math.Max(1, batchSize);
        _maxBatchDelay = maxBatchDelay;
        Name = ArgusUtil.GetTypeNameWithoutGenerics(reducer.GetType());
        _inbox = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(channelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Adds a downstream pipeline. Increments the dependent's expected completion votes.</summary>
    public void AddDependent(ReducerPipeline dependent)
    {
        ArgumentNullException.ThrowIfNull(dependent);
        _dependents.Add(dependent);
        dependent.IncrementExpectedVotes();
    }

    /// <summary>Increments the count of upstream producers that must vote for completion before the inbox closes.</summary>
    public void IncrementExpectedVotes() => _expectedCompletionVotes++;

    /// <summary>Pushes an envelope into this pipeline's inbox. Suspends if the inbox is full (backpressure).</summary>
    public ValueTask EnqueueAsync(Envelope envelope, CancellationToken ct)
        => _inbox.Writer.WriteAsync(envelope, ct);

    /// <summary>
    /// Records one upstream producer's "I'm done" vote. When all expected votes
    /// have arrived, the inbox closes and the run loop drains and exits.
    /// </summary>
    public void Complete()
    {
        int votes = Interlocked.Increment(ref _completionVotes);
        if (votes >= _expectedCompletionVotes)
        {
            _ = _inbox.Writer.TryComplete();
        }
    }

    /// <summary>
    /// The pipeline's run loop. Reads envelopes from the inbox until completion;
    /// processes each envelope through the reducer and forwards/commits per the
    /// per-branch UoW rules described in the class summary.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (Envelope envelope in _inbox.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await ProcessEnvelopeAsync(envelope, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogReducerError(_logger, ex, Name);

                    // The branch UoW may have partial uncommitted writes — roll
                    // back if we can. Don't let the failure leak into other
                    // branches; rethrow to surface the error to the worker.
                    if (envelope.BranchUow is not null)
                    {
                        try { await envelope.BranchUow.RollbackAsync(ct).ConfigureAwait(false); }
                        catch (Exception rollbackEx) { LogRollbackError(_logger, rollbackEx, Name); }
                        await envelope.BranchUow.DisposeAsync().ConfigureAwait(false);
                    }

                    // Batched leaf: the open batch holds this block's partial writes.
                    await DiscardOpenBatchAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            // Persist the final partial batch before signaling completion. Best-effort
            // on shutdown (CancellationToken.None) — a hard abort that loses it is
            // recovered by replay from the last committed checkpoint.
            try { await CommitBatchAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { LogReducerError(_logger, ex, Name); }

            // Cascade completion downstream — each dependent gets one vote per
            // upstream producer, so calling Complete once is correct.
            foreach (ReducerPipeline dep in _dependents)
            {
                dep.Complete();
            }
        }
    }

    private async Task ProcessEnvelopeAsync(Envelope envelope, CancellationToken ct)
    {
        // Batched standalone independent reducer (no parent, no dependents — it owns
        // its UoW): accumulate forward blocks into one UoW and commit on size / delay /
        // drain, so the fsync amortizes across the batch. The BranchUow == null guard
        // excludes a dependency-chain leaf, which receives a forwarded UoW it must
        // commit, not replace. A rollback commits the open batch first (then the normal
        // path below deletes any over-committed tip blocks) — committing, not
        // discarding, so a rollback inside the batch keeps valid pre-fork blocks the
        // chain won't re-deliver. batchSize == 1 skips this entirely (per-block commit).
        if (_batchSize > 1 && _dependents.Count == 0 && envelope.BranchUow is null)
        {
            if (envelope.Response.Action == NextResponseAction.RollForward)
            {
                await ProcessBatchedRollForwardAsync(envelope).ConfigureAwait(false);
                return;
            }
            await CommitBatchAsync(ct).ConfigureAwait(false);
        }

        // Branch-root pipelines see envelope.BranchUow == null and create one;
        // interior pipelines reuse the UoW the parent forwarded.
        bool ownsUow = envelope.BranchUow is null;
        IBlockUnitOfWork uow = envelope.BranchUow ?? await _uowFactory.CreateAsync(ct).ConfigureAwait(false);

        try
        {
            ulong slot = envelope.Response.Action == NextResponseAction.RollBack
                ? envelope.Response.RollbackSlot!.Value
                : envelope.Response.Block!.Header().HeaderBody().Slot();

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (envelope.Response.Action == NextResponseAction.RollForward)
                {
                    await _reducer.RollForwardAsync(envelope.Response.Block!, uow, ct).ConfigureAwait(false);
                    Point point = new(envelope.Response.Block!.Header().Hash(), slot);
                    uow.TrackIntersection(Name, point);
                    _intersectionRecorder?.Invoke(Name, point);
                }
                else
                {
                    ulong rollbackSlot = envelope.Response.RollBackType switch
                    {
                        RollBackType.Exclusive => envelope.Response.RollbackSlot!.Value + 1UL,
                        RollBackType.Inclusive => envelope.Response.RollbackSlot!.Value,
                        _ => 0
                    };
                    await _reducer.RollBackwardAsync(rollbackSlot, uow, ct).ConfigureAwait(false);
                    uow.TrackRollback(Name, rollbackSlot);
                    _rollbackRecorder?.Invoke(Name, rollbackSlot);
                }
            }
            finally
            {
                sw.Stop();
                _telemetryRecorder?.Invoke(Name, sw.ElapsedMilliseconds, slot);
                LatestSlot = slot;
            }

            // Decide what to do with the UoW based on graph topology:
            // - 0 dependents (leaf): commit (or defer) and dispose
            // - 1 dependent (linear): forward same UoW down the chain
            // - >1 dependents (fork): commit (or defer), then spawn fresh UoWs per child
            if (_dependents.Count == 0)
            {
                await CommitOrDeferAsync(uow, envelope.Response.Action == NextResponseAction.RollForward, ct).ConfigureAwait(false);
                ownsUow = false;
                return;
            }

            if (_dependents.Count == 1)
            {
                // Linear continuation — pass UoW ownership down. Don't dispose here.
                await _dependents[0].EnqueueAsync(envelope with { BranchUow = uow }, ct).ConfigureAwait(false);
                ownsUow = false; // dependent owns it now
                return;
            }

            // Fork: commit (or defer) current branch, spawn fresh UoWs per child.
            await CommitOrDeferAsync(uow, envelope.Response.Action == NextResponseAction.RollForward, ct).ConfigureAwait(false);
            ownsUow = false;

            foreach (ReducerPipeline dep in _dependents)
            {
                await dep.EnqueueAsync(envelope with { BranchUow = null }, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // If something threw before we finished forwarding, dispose the UoW
            // we created so it doesn't leak. (When ownsUow == false, the
            // dependent took ownership and is responsible for dispose.)
            if (ownsUow)
            {
                try { await uow.RollbackAsync(ct).ConfigureAwait(false); } catch { /* secondary error */ }
                await uow.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Accumulates one forward block into the open batch UoW (creating it on the
    /// first block of a batch), flushing per block so later blocks in the batch can
    /// read this one within the open transaction, then commits the batch when it is
    /// full, the delay cap elapses, or the inbox has drained (caught up to tip).
    /// </summary>
    private async Task ProcessBatchedRollForwardAsync(Envelope envelope)
    {
        // CancellationToken.None throughout: once a block enters the batch it runs to
        // completion (create + reduce + flush + any triggered commit) so shutdown
        // never interrupts a DB write mid-flight. Cancellation is observed only
        // between blocks, at the inbox read in RunAsync — which then commits the
        // final partial batch in the run loop's finally.
        if (_batchUow is null)
        {
            _batchUow = await _uowFactory.CreateAsync(CancellationToken.None).ConfigureAwait(false);
            _batchCount = 0;
            _batchStartTimestamp = Stopwatch.GetTimestamp();
        }

        ulong slot = envelope.Response.Block!.Header().HeaderBody().Slot();
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await _reducer.RollForwardAsync(envelope.Response.Block!, _batchUow, CancellationToken.None).ConfigureAwait(false);
            Point point = new(envelope.Response.Block!.Header().Hash(), slot);
            _batchUow.TrackIntersection(Name, point);
            _intersectionRecorder?.Invoke(Name, point);
            // Flush (no commit/fsync) so this block's writes are visible to later
            // blocks in the batch via a query within the open transaction.
            await _batchUow.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            _telemetryRecorder?.Invoke(Name, sw.ElapsedMilliseconds, slot);
            LatestSlot = slot;
        }

        _batchCount++;

        bool full = _batchCount >= _batchSize;
        bool delayElapsed = _maxBatchDelay > TimeSpan.Zero
            && Stopwatch.GetElapsedTime(_batchStartTimestamp) >= _maxBatchDelay;
        bool drained = _inbox.Reader.Count == 0;
        if (full || delayElapsed || drained)
        {
            await CommitBatchAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Commits the open batch UoW (an all-empty batch defers, exactly like per-block
    /// empty deferral) and clears batch state. No-op when no batch is open.
    /// </summary>
    private async Task CommitBatchAsync(CancellationToken ct)
    {
        if (_batchUow is null)
        {
            return;
        }
        IBlockUnitOfWork uow = _batchUow;
        _batchUow = null;
        _batchCount = 0;
        await CommitOrDeferAsync(uow, deferIfEmpty: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rolls back and disposes the open batch UoW (error path) and clears batch
    /// state. No-op when no batch is open.
    /// </summary>
    private async Task DiscardOpenBatchAsync()
    {
        if (_batchUow is null)
        {
            return;
        }
        IBlockUnitOfWork uow = _batchUow;
        _batchUow = null;
        _batchCount = 0;
        // Best-effort cleanup on None — the fault path may run with an already-
        // cancelled ambient token.
        try { await uow.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) { LogRollbackError(_logger, ex, Name); }
        await uow.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Commits the UoW with deferral semantics: if the reducer(s) tracked no
    /// data changes for this block, the UoW skips its DB write entirely and the
    /// pending intersections are stashed for the next data-bearing block. Carries
    /// over previously-deferred intersections so the next commit advances state
    /// for all blocks since the last on-disk checkpoint.
    /// </summary>
    private async Task CommitOrDeferAsync(IBlockUnitOfWork uow, bool deferIfEmpty, CancellationToken ct)
    {
        if (deferIfEmpty)
        {
            // Carry over intersections deferred from previous no-op blocks.
            // Current block checkpoints win over older deferred checkpoints.
            foreach ((string name, Point point) in _pendingDeferred)
            {
                if (!uow.TrackedIntersections.TryGetValue(name, out Point? current) || current.Slot < point.Slot)
                {
                    uow.TrackIntersection(name, point);
                }
            }
        }
        else
        {
            // Rollbacks invalidate any deferred forward checkpoints for this
            // branch; the rollback checkpoint rewind is committed immediately.
            _pendingDeferred.Clear();
        }

        // Snapshot in case the commit defers; TrackedIntersections is the
        // UoW's live view and gets cleared on a real commit.
        Dictionary<string, Point> snapshot = new(uow.TrackedIntersections);

        bool committed = await uow.CommitAsync(deferIfEmpty, ct).ConfigureAwait(false);

        if (committed)
        {
            _pendingDeferred.Clear();
        }
        else
        {
            // No data was written; stash all currently-known intersections
            // for the next data-bearing block. Newer points overwrite older
            // ones for the same reducer (assignment semantics).
            _pendingDeferred.Clear();
            foreach ((string name, Point point) in snapshot)
            {
                _pendingDeferred[name] = point;
            }
        }

        await uow.DisposeAsync().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Pipeline {Reducer} failed processing envelope")]
    private static partial void LogReducerError(ILogger logger, Exception ex, string reducer);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to roll back UoW after pipeline {Reducer} error")]
    private static partial void LogRollbackError(ILogger logger, Exception ex, string reducer);
}
