# Argus.Sync.Bench

Benchmark prototype for the Argus rearchitecture (plan: `~/.claude/plans/lexical-snuggling-quilt.md`).

## What this measures

The current `CardanoIndexWorker` uses a strict sequential `await foreach` chain pump (`CardanoIndexWorker.cs:284`) that pulls one block, processes it through the entire dependency tree (`Task.WhenAll(forward) + recurse`), then pulls the next. Live testing showed this caps a 3-deep dependency chain at ~274 slots/sec while a standalone root reducer in the same process runs at ~8,200 — a 30× slowdown attributable purely to the cascade.

The proposed rearchitecture replaces this with bounded `System.Threading.Channels.Channel<Envelope>` per reducer + per-reducer run loop. The chain consumer pumps blocks into root channels and immediately pulls the next, giving pipeline parallelism (block N at depth-2 while N+1 is at depth-1 while N+2 is at root). Bounded capacity (default 256) + `BoundedChannelFullMode.Wait` provides cooperative backpressure with hard memory bounds.

This bench is the **perf baseline** — quantify the gap between the two impls so we have evidence the rearchitecture solves what it claims to solve.

## Why Channels (not TPL Dataflow)

Settled by research before benching:
- Stephen Toub's canonical benchmark: Channels 22× faster than `BufferBlock<T>`, 0 allocations vs 7.36 GB. ([source](https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/))
- Independent 2023 benchmark (Charles Chen): Channels 12× faster than `Parallel.ForEachAsync`. ([source](https://chrlschn.dev/blog/2023/10/dotnet-task-parallel-library-vs-system-threading-channels/))
- Microsoft's own pattern: Channels for producer/consumer; Dataflow only when you need automatic graph-linking with two-phase commit.
- `BroadcastBlock` is broken for backpressure (drops data when consumers lag). Hard disqualifier for fan-out.
- Cardano-indexer peers (Oura, Scrolls, Carp) all use bounded-queue-per-stage. Validated pattern.
- No newer .NET primitive exists — Project Vienna (green threads) was killed late 2023; runtime-async/async2 in .NET 10/11 makes existing async cheaper but doesn't replace Channels.

## Topologies

| Topology | Shape | Maps to |
|---|---|---|
| `SingleRoot` | one reducer, no dependents | producer/consumer overhead in isolation |
| `LinearDepth3` | `R → A → B` | `BlockTestReducer → DependentTransactionReducer → ChainedDependentReducer` (the slow case in the live test) |
| `Tree` | `R → {A → A1, B}` | sibling-parallel + chain |

## Work profiles

| Profile | Cost model | Mirrors |
|---|---|---|
| `CpuLight` | `await Task.Yield()` | pure framework overhead — measures async-state-machine + channel ops |
| `DbRealistic` | `await Task.Delay(3ms)` | one Postgres loopback round-trip per block |
| `CpuHeavy` | `Thread.SpinWait(50_000)` + small alloc | CBOR deserialization (not in default suite) |
| `BulkWrite` | 20ms per 100 blocks | `EFCore.BulkExtensions` semantics (not in default suite) |

## Acceptance gate (Phase 3 of the rearchitecture)

`Channels_LinearDepth3` at `DbRealistic` profile must run at ≥3,000 envelopes/sec sustained. Current measured baseline (live preview node, real reducers): 274 sl/s. Target is 11× improvement minimum, structurally enforced by the dependency depth.

## Results

_(Populated by `dotnet run -c Release -- --filter '*PipelineBenchmarks*'`. Latest run pasted below.)_

```
TBD — bench in progress
```

## Running

```bash
# Full suite
cd src/Argus.Sync.Bench
dotnet run -c Release -- --filter '*PipelineBenchmarks*'

# Smoke test (functional check, ~1 second)
dotnet run -c Release -- smoke
```

## Files

| File | Purpose |
|---|---|
| `Workload/Envelope.cs` | Synthetic block (slot, height, action, payload bytes). Sized to mirror real Cardano blocks. |
| `Workload/SyntheticChain.cs` | Pre-generates a fixed envelope list — isolates pipeline overhead from chain I/O. |
| `Workload/Reducer.cs` | Parameterized async work delegate (`WorkProfile` enum). |
| `Workload/Topology.cs` | Dependency graph shapes. |
| `Pipelines/IPipeline.cs` | Common contract for both impls. |
| `Pipelines/CascadePipeline.cs` | Faithful reproduction of `CardanoIndexWorker.cs:284`'s `await foreach + Task.WhenAll(forward) + recurse`. |
| `Pipelines/ChannelsPipeline.cs` | Per-reducer bounded `Channel<Envelope>` + run loop + completion-vote propagation. |
| `Pipelines/SmokeTest.cs` | Functional check. |
| `Benchmarks/PipelineBenchmarks.cs` | BenchmarkDotNet entry. |
