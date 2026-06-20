using System.Threading.Channels;
using Argus.Sync.Bench.Workload;

namespace Argus.Sync.Bench.Pipelines;

/// <summary>
/// Per-reducer bounded <see cref="Channel{T}"/> with a dedicated run loop per
/// reducer and explicit completion propagation. The chain consumer pumps
/// envelopes into root channels and immediately moves to the next envelope
/// without waiting on the dependency tree, giving pipeline parallelism:
/// while block N is at depth-2, N+1 is at depth-1, N+2 is at root, and N+3 is
/// being pulled from the chain.
///
/// Bounded backpressure: when a slow reducer's channel is full, its parent's
/// <c>WriteAsync</c> *suspends* (not drops, not throws). Suspension propagates
/// upstream to the chain consumer, which stops pulling. Memory cannot grow
/// past <c>Σ (capacity × envelope-size)</c>.
/// </summary>
public sealed class ChannelsPipeline(int channelCapacity = 256) : IPipeline
{
    private readonly int _channelCapacity = channelCapacity;

    public string Name => $"Channels (cap={_channelCapacity})";

    public async Task RunAsync(
        Topology topology,
        IReadOnlyList<Envelope> envelopes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(envelopes);

        // Build per-node ReducerStage instances. Each stage owns a channel +
        // run loop + a list of dependent stages it must forward to.
        Dictionary<TopologyNode, ReducerStage> stages = [];
        foreach (TopologyNode node in topology.AllNodes())
        {
            stages[node] = new ReducerStage(
                new BenchReducer(node.Name, node.Profile),
                _channelCapacity);
        }

        // Wire dependents (parent → list of dependent stages). AddDependent
        // also increments the dependent's expected-completion-votes so we
        // know how many upstream producers must vote before the inbox closes.
        foreach ((TopologyNode node, ReducerStage stage) in stages)
        {
            foreach (TopologyNode dependent in node.Dependents)
            {
                stage.AddDependent(stages[dependent]);
            }
        }

        // The chain consumer is itself a producer for every root — register
        // that explicitly so the root's vote-count expects exactly one vote
        // from us (called below) on top of any upstream stages.
        foreach (TopologyNode root in topology.Roots)
        {
            stages[root].IncrementExpectedVotes();
        }

        // Start every reducer's run loop. They block on their inbox channel.
        Task[] runTasks = [.. stages.Values.Select(s => s.RunAsync(ct))];

        // Chain consumer: push every envelope into every root's channel.
        // No waiting on dependents — that's the whole point.
        try
        {
            foreach (Envelope env in envelopes)
            {
                ct.ThrowIfCancellationRequested();
                foreach (TopologyNode root in topology.Roots)
                {
                    await stages[root].EnqueueAsync(env, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            // Signal end-of-stream to roots; completion cascades downstream.
            foreach (TopologyNode root in topology.Roots)
            {
                stages[root].Complete();
            }
        }

        await Task.WhenAll(runTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Per-reducer stage: bounded inbox channel, run loop that pulls from the
    /// channel and forwards to dependents after processing, completion that
    /// cascades downstream when the inbox completes (chain producer EOF).
    /// </summary>
    private sealed class ReducerStage
    {
        private readonly BenchReducer _reducer;
        private readonly Channel<Envelope> _inbox;
        private readonly List<ReducerStage> _dependents = [];
        private int _completionVotes;
        // Number of upstream producers that must vote before the inbox closes.
        // 0 is the right default: a stage with zero parents and not directly
        // fed by the chain consumer should never close (this never happens —
        // all stages have at least one producer — but the explicit accounting
        // is what prevents the previous deadlock where dependents had a
        // phantom default vote that the chain consumer never delivered).
        private int _expectedCompletionVotes;

        public ReducerStage(BenchReducer reducer, int capacity)
        {
            _reducer = reducer;
            _inbox = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        }

        public void AddDependent(ReducerStage dependent)
        {
            _dependents.Add(dependent);
            dependent.IncrementExpectedVotes();
        }

        public void IncrementExpectedVotes() => _expectedCompletionVotes++;

        public ValueTask EnqueueAsync(Envelope env, CancellationToken ct) =>
            _inbox.Writer.WriteAsync(env, ct);

        /// <summary>
        /// Mark this stage as completed by one upstream producer. When all
        /// expected upstream producers have voted, the inbox is closed and the
        /// run loop exits naturally.
        /// </summary>
        public void Complete()
        {
            int votes = Interlocked.Increment(ref _completionVotes);
            if (votes >= _expectedCompletionVotes)
            {
                _inbox.Writer.TryComplete();
            }
        }

        public async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await foreach (Envelope env in _inbox.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await _reducer.ProcessAsync(env, ct).ConfigureAwait(false);

                    // Forward to dependents after processing — this is what
                    // structurally enforces parent-before-dependent ordering.
                    foreach (ReducerStage dep in _dependents)
                    {
                        await dep.EnqueueAsync(env, ct).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                // Cascade completion downstream — each dependent expects one
                // vote from each of its parents, so calling Complete once per
                // upstream stage is correct.
                foreach (ReducerStage dep in _dependents)
                {
                    dep.Complete();
                }
            }
        }
    }
}
