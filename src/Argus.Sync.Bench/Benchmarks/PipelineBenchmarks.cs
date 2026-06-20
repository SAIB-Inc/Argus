using Argus.Sync.Bench.Pipelines;
using Argus.Sync.Bench.Workload;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Argus.Sync.Bench.Benchmarks;

/// <summary>
/// Compares the current cascade-of-awaits pipeline against the proposed
/// bounded-channels pipeline across the dep-graph topologies that matter
/// for Argus, under DB-realistic per-reducer cost. Establishes a perf
/// baseline for the rearchitecture's Phase 3 acceptance gate.
/// </summary>
[Config(typeof(Config))]
[MemoryDiagnoser]
public class PipelineBenchmarks
{
    private sealed class Config : ManualConfig
    {
        public Config() =>
            // ShortRunJob keeps wall time reasonable while still doing
            // 3 warmup + 3 measurement iterations — enough to stabilize for
            // a comparative bench (we care about ratio between impls more
            // than absolute ns precision).
            AddJob(Job.ShortRun);
    }

    [Params(2_000)]
    public int BlockCount { get; set; }

    [Params(WorkProfile.CpuLight, WorkProfile.DbRealistic)]
    public WorkProfile Profile { get; set; }

    private IReadOnlyList<Envelope> _envelopes = [];
    private IPipeline _cascade = null!;
    private IPipeline _channels = null!;

    [GlobalSetup]
    public void Setup()
    {
        _envelopes = SyntheticChain.Generate(BlockCount, payloadBytes: 3072);
        _cascade = new CascadePipeline();
        _channels = new ChannelsPipeline(channelCapacity: 256);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("LinearDepth3")]
    public Task Cascade_LinearDepth3() =>
        _cascade.RunAsync(Topology.LinearDepth3(Profile), _envelopes, CancellationToken.None);

    [Benchmark]
    [BenchmarkCategory("LinearDepth3")]
    public Task Channels_LinearDepth3() =>
        _channels.RunAsync(Topology.LinearDepth3(Profile), _envelopes, CancellationToken.None);

    [Benchmark]
    [BenchmarkCategory("SingleRoot")]
    public Task Cascade_SingleRoot() =>
        _cascade.RunAsync(Topology.SingleRoot(Profile), _envelopes, CancellationToken.None);

    [Benchmark]
    [BenchmarkCategory("SingleRoot")]
    public Task Channels_SingleRoot() =>
        _channels.RunAsync(Topology.SingleRoot(Profile), _envelopes, CancellationToken.None);

    [Benchmark]
    [BenchmarkCategory("Tree")]
    public Task Cascade_Tree() =>
        _cascade.RunAsync(Topology.Tree(Profile), _envelopes, CancellationToken.None);

    [Benchmark]
    [BenchmarkCategory("Tree")]
    public Task Channels_Tree() =>
        _channels.RunAsync(Topology.Tree(Profile), _envelopes, CancellationToken.None);
}
