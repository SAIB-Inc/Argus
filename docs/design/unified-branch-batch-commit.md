# Design note: unified sequential branches + batch-commit

Status: **proposal** (for review before implementation)
Branch: `feat/batch-commit`
Supersedes the fork/per-branch model for dependent reducers. The shipped standalone
batch-commit (`feat/batch-commit`) is the same mechanism applied to one reducer; this
generalizes it to the whole dependency graph by **deleting the fork special-case**.

---

## TL;DR

Today a reducer with >1 dependents is a **fork**: the parent commits, then spawns a
fresh unit-of-work per child so the children can run on separate cores. That optimizes
**CPU parallelism across children** — but a Cardano indexer is **fsync-bound**, not
CPU-bound, so it optimizes the axis that isn't the bottleneck, while paying for it with
separate `DbContext`s (no shared `.Local`), N commits per block (N fsyncs), and a
lagging-checkpoint recovery problem.

**Proposal:** every root's dependency graph becomes **one sequential branch sharing one
batched unit-of-work**. Reducers run in topological order per block into the same
`DbContext` (read `.Local` or query the DB), and the batch triggers
(`size` / `delay` / `drain-at-tip`) commit the whole graph at once.

Net effect for a Levvy-shaped graph (6 reducers): **~6 fsyncs/block → 1 fsync per K
blocks**, `.Local` works across every reducer, the graph commits atomically (recovery
simplifies to a single checkpoint), and batching is uniform — no
standalone-vs-chain-vs-fork distinction. The parallelism we give up (children on
separate cores) is replaced by the parallelism that actually matters here: overlapping
the **commit (fsync)** of batch N with the **processing** of batch N+1.

---

## Motivation

The whole-DB-with-real-reducers benchmark (this branch):

| stack | N2C blk/s | bottleneck |
|---|---:|---|
| Argus + NoOp sink | ~1,900 | framework/decode |
| Argus → Postgres (per-block) | ~220 | **per-block fsync** |
| Argus → Postgres (batch 500) | ~1,013 | flush round-trips |

The per-block fsync is the wall. Batch-commit amortizes it for a single reducer (4.6×
Postgres, 6.8× Mongo). The remaining gap is dependent reducers — and that is *most* of a
real app: Levvy's graph is `OutputBySlotReducer` → {`LevvyActiveUTxO`, `UserStats`,
`Leaderboard`, `PlatformStats`} plus `LevvyActiveUTxO` → `LendPositions` (a fork of 4 +
one linear tail). Under the current model that graph pays a commit (fsync) **per branch
per block**.

## Current model (what we change)

- **Per-block, per-branch UoW.** A "branch" is a maximal linear chain. The branch root
  creates a `DbContext`+transaction; interior nodes **forward the same UoW** to their one
  dependent (downstream reads upstream via the shared change-tracker, `ctx.X.Local`); the
  leaf commits it.
- **Fork (>1 dependents):** the parent commits its UoW first, then spawns a **fresh UoW
  per child**. Children are independent branches with their own contexts; they read the
  parent's *committed* data. They commit independently → their checkpoints can diverge.
- **Pipeline:** each reducer is its own task + bounded channel. Parallelism is
  stage-overlap (`root(N+1)` ‖ `child(N)`) and fork-fan-out (`A` ‖ `B`).
- **Recovery:** per-reducer checkpoints; on restart the worker resyncs a root from the
  **oldest** dependent's checkpoint (safe-intersection), re-feeding the rest idempotently.

Why the fork exists: independent children on separate cores. Why it hurts here: separate
contexts (no cross-child `.Local`), one fsync per branch per block, and the divergent
checkpoints make crash-recovery subtle (this is the exact interaction that broke
`ForkDependentCrashRecoveryTest` when standalone batching was first applied too broadly).

## Proposed model

**One root chain-consumer → one sequential branch over the entire reachable dependency
subgraph, sharing one batched UoW.**

- A block is processed through **all** reducers in the graph in **topological order**
  (parents before children), all writing the **same** `DbContext`.
- Cross-reducer reads are unchanged from the reducer's point of view: `.Local` (sees the
  batch's accumulated, not-yet-committed writes) or a DB query for misses (sees prior
  blocks in the batch — flushed into the open transaction).
- The UoW spans up to `Sync:Commit:BatchSize` blocks. The triggers
  (size **or** `Sync:Commit:MaxDelayMs` **or** inbox-drained-at-tip) commit the whole
  graph's accumulated writes + every reducer's checkpoint in **one** transaction.
- **No fork path.** A reducer with N dependents simply has its dependents run later in the
  same topological pass on the same context. Independent siblings run back-to-back
  (sequentially), not concurrently.

### Execution

Per batch (one open UoW, fresh `DbContext`+transaction):

```
for each block N in the batch:
    for each reducer R in topological order:
        R.RollForward(blockN, uow)        # R reads .Local / queries; writes uow
        uow.TrackIntersection(R, pointN)
    uow.Flush()                            # SaveChanges, no commit; bounds the tracker,
                                           #   makes block N visible to N+1 via the txn
    if (count >= BatchSize) or (elapsed >= MaxDelay) or (inbox drained):
        commit this UoW (one fsync, all reducers, all K blocks, all checkpoints)
        open a fresh UoW for the next batch
```

Rollback: commit the open batch first (so a rollback inside the batch keeps valid
pre-fork blocks), then apply `RollBackward` through the graph and commit — same rule the
standalone path already uses.

### Where the parallelism goes

We lose intra-graph concurrency (fork fan-out + stage overlap) and replace it with
**cross-batch pipelining**: while batch N's UoW is flushing/committing to disk (async
I/O), batch N+1 is already decoding + reducing on a *new* UoW. This hides the one
expensive thing (the fsync) behind useful work — the right thing to overlap when you're
fsync-bound. Bound it to ~1–2 batches in flight (a process-stage and a commit-stage) so
memory and the crash-replay window stay bounded.

### Recovery

The graph commits **atomically** → after any commit, every reducer is at the same slot.
Consequences:

- **One checkpoint** per graph (still stored per-reducer for resume, but always equal).
- The **safe-intersection / oldest-dependent** logic collapses — there is no divergence to
  reconcile. The fork-recovery wrinkle disappears by construction.
- Crash mid-batch → the open (uncommitted) batch is dropped → resume from the last
  committed checkpoint → re-process (idempotent, same requirement reducers already have
  for rollbacks). Replay window ≤ `BatchSize` blocks.

(Backfill of a newly-added reducer still works: it starts behind, the graph resyncs from
the oldest point, and the caught-up reducers re-process idempotently — same as today, just
without divergent steady-state checkpoints.)

## Levvy walk-through

Graph: `OutputBySlot` → {`LevvyActiveUTxO`, `UserStats`, `Leaderboard`, `PlatformStats`};
`LevvyActiveUTxO` → `LendPositions`. Topological order e.g.
`OutputBySlot, LevvyActiveUTxO, LendPositions, UserStats, Leaderboard, PlatformStats`.

- **Per block:** all 6 run in order into one `DbContext`. `LevvyActiveUTxO` reads
  `OutputBySlot`'s writes via `.Local`; `LendPositions` reads `LevvyActiveUTxO` via
  `.Local`; the stats reducers read `OutputBySlot` via `.Local`, batch-DB for misses.
  Every read pattern Levvy already uses works unchanged.
- **Commit:** one transaction per K blocks covers all 6 reducers' writes + checkpoints.
- **fsync count:** today ≈ 6 per block (root commit + 4 fork children + the tail). New: 1
  per K blocks. For K=500 that is a ~3000× reduction in fsyncs over the same span.

## Gained vs lost

| | Current (fork + per-block) | Proposed (unified branch + batch) |
|---|---|---|
| fsyncs | N branches × per block | 1 per batch, whole graph |
| `.Local` across reducers | only within a shared branch | everywhere in the graph |
| Checkpoints | per-reducer, can diverge | atomic, single slot |
| Crash recovery | safe-intersection reconcile | resume one checkpoint |
| Batching | standalone only (this branch) | uniform, all reducers |
| Code | fork commit-then-spawn special-case | deleted |
| Intra-graph CPU parallelism | yes (fork fan-out, stage overlap) | **no** (sequential) |
| fsync ↔ next-batch overlap | n/a | **yes** (cross-batch pipeline) |

**The one real loss:** a wide fan-out of *CPU-heavy* children (many siblings each doing
real computation) can no longer run on separate cores. Mitigations if a profile ever
needs it: optional opt-in parallelism for a marked sub-fork (separate contexts, accept
the divergent checkpoint), or shard the indexer. Not the indexer norm.

## Reducer contract (unchanged)

No reducer changes required. The contract is what it already is, now load-bearing because
batching is default-on:

- Read/write through `uow.GetStorage<T>()` (the batch's `DbContext`/session) — `.Local`
  for same-batch writes, a DB query for misses (resolved within the open transaction).
- Do **not** assume a block is durably committed when `RollForward` returns (no per-block
  external side-effects); durability is at the batch commit.
- Stay idempotent on replay (already required for rollbacks).

## Risks & open questions

1. **Mongo transaction limits.** One session spanning (graph × K blocks) of writes must
   stay under ~16 MB / op-count. Need a size-aware commit (flush+commit when approaching
   the cap) or a lower default `BatchSize` for Mongo.
2. **Change-tracker growth (EF).** The shared context accumulates all reducers' writes for
   a block before flush. Flush-per-block + `ChangeTracker.Clear()` bounds it (already done
   in the standalone path); confirm cross-reducer `.Local` reads all happen *before* the
   per-block flush (they do — flush is end-of-block).
3. **Topological order** must be stable and cycle-free (the graph builder already rejects
   cycles); define a deterministic order for sibling reducers.
4. **Cross-batch pipeline depth + backpressure** — cap in-flight batches so processing
   can't outrun commits (memory + replay-window bound).
5. **Per-reducer independence** — reducers in one graph now share a commit cadence and
   checkpoint; a use-case needing truly independent lifecycles per reducer would need them
   in separate root graphs.
6. **Wide CPU-heavy fan-out** — the accepted loss above; revisit only if profiling shows
   it.

## Validation / acceptance test

Head-to-head, **published NuGet `1.1.0-alpha` (current fork-based, per-block) vs the new
unified architecture (local build)**, on the *same* consumer with a **dependent-reducer
graph**:

- A test consumer with a representative chain/fork — e.g. a root `BlockReducer` →
  `DependentReducer` (reads root via `.Local`, writes) → optionally a fan-out — mirroring
  Levvy's shape. Same DB, same fixed start point, same 5000-block measure, fresh DB each
  run (the existing `/tmp/argus-bench` harness pattern).
- Run A: consumer pinned to `Argus.Sync 1.1.0-alpha` from nuget.org.
- Run B: consumer ProjectReference'd to the new local build.
- Expect: B ≫ A on the dependent graph (the fsync collapse), and B's committed-row /
  final-state assertions identical to A's (correctness parity).
- Plus: the existing rollback/crash/fork EndToEnd suite green under the new model, and a
  burst test that floods N blocks → asserts a single batched commit for the whole graph →
  rolls back mid-batch.

Acceptance: correctness parity with `1.1.0-alpha` on the dependent graph **and** a clear
throughput win, with the EndToEnd suite green.

## Rollout

1. Land standalone batch-commit (done on this branch) — the mechanism.
2. Replace the fork/per-branch UoW with the unified sequential-branch UoW + cross-batch
   pipeline (this note).
3. Validate per the acceptance test above.
4. Release as the next Argus alpha; reducers need no changes.
