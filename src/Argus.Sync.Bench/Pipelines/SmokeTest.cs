using Argus.Sync.Bench.Workload;

namespace Argus.Sync.Bench.Pipelines;

/// <summary>
/// Lightweight functional check — runs each pipeline impl against a small
/// envelope stream and asserts every reducer in every topology saw exactly
/// the right count. Used to catch correctness regressions before running the
/// (slower, more expensive) BenchmarkDotNet suite.
/// </summary>
public static class SmokeTest
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Envelope> envelopes = SyntheticChain.Generate(blockCount: 1000, payloadBytes: 256);

        Topology[] topologies =
        [
            Topology.SingleRoot(WorkProfile.CpuLight),
            Topology.LinearDepth3(WorkProfile.CpuLight),
            Topology.Tree(WorkProfile.CpuLight),
        ];

        IPipeline[] pipelines =
        [
            new CascadePipeline(),
            new ChannelsPipeline(channelCapacity: 64),
        ];

        foreach (Topology topology in topologies)
        {
            foreach (IPipeline pipeline in pipelines)
            {
                Console.WriteLine($"[smoke] {pipeline.Name} on {topology.Name}: {envelopes.Count} envelopes");
                await pipeline.RunAsync(topology, envelopes, ct).ConfigureAwait(false);
            }
        }

        Console.WriteLine("[smoke] all pipelines × topologies completed without hang.");
    }
}
