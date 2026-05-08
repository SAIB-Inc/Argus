using Argus.Sync.Bench.Workload;

namespace Argus.Sync.Bench.Pipelines;

/// <summary>
/// Faithful reproduction of the current <c>CardanoIndexWorker.cs:284</c>
/// dependency-forwarding shape:
/// <code>
///   await foreach (env in producer)        // line 284 — sequential pump
///   {
///       await rootReducer.ProcessAsync(env); // line 294 — wait for root
///       await Task.WhenAll(                  // line 407 — siblings parallel
///           dependents.Select(d =&gt; ProcessDependent(d, env, ...))
///       );
///   }
///   // ProcessDependent calls itself recursively for chained dependents
///   // (line 465) — inner awaits serialize per chain.
/// </code>
/// Block N+1 cannot be pulled until block N's entire dependency tree completes.
/// This is the bottleneck the rearchitecture targets.
/// </summary>
public sealed class CascadePipeline : IPipeline
{
    public string Name => "Cascade (current)";

    public async Task RunAsync(
        Topology topology,
        IReadOnlyList<Envelope> envelopes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(envelopes);

        Dictionary<TopologyNode, BenchReducer> reducers = topology
            .AllNodes()
            .ToDictionary(n => n, n => new BenchReducer(n.Name, n.Profile));

        foreach (Envelope env in envelopes)
        {
            ct.ThrowIfCancellationRequested();

            // Mirror the worker: process each root in parallel (siblings),
            // each root's subtree recurses serially down its chain but
            // parallel across siblings at every level.
            Task[] rootTasks = new Task[topology.Roots.Count];
            for (int i = 0; i < topology.Roots.Count; i++)
            {
                rootTasks[i] = ProcessSubtreeAsync(topology.Roots[i], reducers, env, ct);
            }

            await Task.WhenAll(rootTasks).ConfigureAwait(false);
        }
    }

    private static async Task ProcessSubtreeAsync(
        TopologyNode node,
        IReadOnlyDictionary<TopologyNode, BenchReducer> reducers,
        Envelope env,
        CancellationToken ct)
    {
        await reducers[node].ProcessAsync(env, ct).ConfigureAwait(false);

        if (node.Dependents.Count == 0)
        {
            return;
        }

        Task[] dependentTasks = new Task[node.Dependents.Count];
        for (int i = 0; i < node.Dependents.Count; i++)
        {
            dependentTasks[i] = ProcessSubtreeAsync(node.Dependents[i], reducers, env, ct);
        }

        await Task.WhenAll(dependentTasks).ConfigureAwait(false);
    }
}
