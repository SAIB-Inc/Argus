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

    private int _completionVotes;
    private int _expectedCompletionVotes;

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
        Action<string, ulong>? rollbackRecorder = null)
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
                    throw;
                }
            }
        }
        finally
        {
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
        // Branch-root pipelines see envelope.BranchUow == null and create one;
        // interior pipelines reuse the UoW the parent forwarded.
        bool ownsUow = envelope.BranchUow is null;
        IBlockUnitOfWork uow = envelope.BranchUow ?? _uowFactory.Create();

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
            // - 0 dependents (leaf): commit and dispose
            // - 1 dependent (linear): forward same UoW down the chain
            // - >1 dependents (fork): commit, then spawn fresh UoWs per child
            if (_dependents.Count == 0)
            {
                await uow.CommitAsync(ct).ConfigureAwait(false);
                await uow.DisposeAsync().ConfigureAwait(false);
                return;
            }

            if (_dependents.Count == 1)
            {
                // Linear continuation — pass UoW ownership down. Don't dispose here.
                await _dependents[0].EnqueueAsync(envelope with { BranchUow = uow }, ct).ConfigureAwait(false);
                ownsUow = false; // dependent owns it now
                return;
            }

            // Fork — commit current branch, spawn fresh UoWs per child.
            await uow.CommitAsync(ct).ConfigureAwait(false);
            await uow.DisposeAsync().ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Pipeline {Reducer} failed processing envelope")]
    private static partial void LogReducerError(ILogger logger, Exception ex, string reducer);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to roll back UoW after pipeline {Reducer} error")]
    private static partial void LogRollbackError(ILogger logger, Exception ex, string reducer);
}
