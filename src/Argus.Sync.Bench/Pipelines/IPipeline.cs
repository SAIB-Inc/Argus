using Argus.Sync.Bench.Workload;

namespace Argus.Sync.Bench.Pipelines;

/// <summary>
/// Pipeline contract used by the bench. Each impl wires a topology of
/// <see cref="BenchReducer"/> instances and exposes a single
/// <see cref="RunAsync"/> entrypoint that pumps an envelope sequence through
/// the graph and returns when every leaf reducer has processed the last block.
/// </summary>
public interface IPipeline
{
    string Name { get; }

    Task RunAsync(
        Topology topology,
        IReadOnlyList<Envelope> envelopes,
        CancellationToken ct);
}
