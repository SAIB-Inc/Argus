using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Tests.Mocks;
using Argus.Sync.Workers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using IBlock = Chrysalis.Codec.Types.Cardano.Core.IBlock;

namespace Argus.Sync.Tests.Unit;

/// <summary>
/// Drives <see cref="ReducerGraphProcessor"/> directly against the committed TestData blocks with an
/// in-memory unit of work — no node, no database (CI-runnable). Pre-loading the inbox before
/// <c>RunAsync</c> makes the inbox-drained trigger deterministic, so we can pin the two batch-commit
/// behaviors the 1.2 architecture introduced: a fault rolls back the WHOLE open batch (not just the
/// faulting block), and a partial batch commits as soon as the inbox drains (the at-tip trigger),
/// without waiting for the size or delay triggers.
/// </summary>
public sealed class ReducerGraphBatchCommitTest(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task FaultMidBatch_RollsBackTheWholeOpenBatch_NothingCommits()
    {
        IBlock[] blocks = LoadBlocks(3);
        if (blocks.Length < 3)
        {
            _output.WriteLine("SKIP: TestData/Blocks not present.");
            return;
        }
        ulong crashSlot = blocks[2].Header().HeaderBody().Slot();

        RecordingBackend backend = new();
        ReducerGraphProcessor processor = new(
            [new StagingReducer(), new CrashOnSlotReducer(crashSlot)],
            new FakeUnitOfWorkFactory(backend),
            channelCapacity: 64,
            batchSize: 10,                            // > 3 blocks, so only the fault can end the batch
            maxBatchDelay: TimeSpan.FromMinutes(10),  // delay trigger cannot fire in a ms-long test
            NullLogger.Instance);

        // Pre-load all three so the crashing (third) block shares ONE open batch with the first two
        // (the inbox is never empty until the crash, so the drained trigger can't commit them early).
        foreach (IBlock block in blocks)
        {
            await processor.EnqueueAsync(new NextResponse(NextResponseAction.RollForward, null, block), CancellationToken.None);
        }
        processor.Complete();

        _ = await Assert.ThrowsAnyAsync<Exception>(() => processor.RunAsync(CancellationToken.None));

        // Whole-batch atomicity: the fault discarded the open batch — none of the three blocks survived,
        // including the two that ran cleanly before the crash.
        Assert.Empty(backend.Committed);
    }

    [Fact]
    public async Task DrainAtTip_CommitsAPartialBatch_WithoutWaitingForSizeOrDelay()
    {
        IBlock[] blocks = LoadBlocks(2);
        if (blocks.Length < 2)
        {
            _output.WriteLine("SKIP: TestData/Blocks not present.");
            return;
        }

        RecordingBackend backend = new();
        ReducerGraphProcessor processor = new(
            [new StagingReducer()],
            new FakeUnitOfWorkFactory(backend),
            channelCapacity: 64,
            batchSize: 500,                           // far more than 2 — the size trigger cannot fire
            maxBatchDelay: TimeSpan.FromMinutes(10),  // nor the delay trigger
            NullLogger.Instance);

        foreach (IBlock block in blocks)
        {
            await processor.EnqueueAsync(new NextResponse(NextResponseAction.RollForward, null, block), CancellationToken.None);
        }
        processor.Complete();
        await processor.RunAsync(CancellationToken.None);

        // Only the inbox-drained (at-tip) trigger could have committed — both blocks landed promptly,
        // in chain order, despite a batch size of 500.
        ulong[] expected = [.. blocks.Select(b => b.Header().HeaderBody().Slot())];
        Assert.Equal(expected, backend.Committed);
    }

    private static IBlock[] LoadBlocks(int count)
    {
        string testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            return [];
        }
        MockChainSyncProvider probe = new(testDataDir);
        return [.. probe.AvailableBlocks.Take(count)];
    }

    // ----- In-memory unit of work: reducers stage slots; a commit moves them to the durable record,
    //       a rollback drops them. FlushAsync stays the default no-op so staged writes accumulate
    //       across the blocks of an open batch (read-your-own-writes), exactly like the real backends. -----

    private sealed class RecordingBackend
    {
        public List<ulong> Committed { get; } = [];
    }

    private sealed class TestStore
    {
        public List<ulong> Staged { get; } = [];
    }

    private sealed class FakeUnitOfWorkFactory(RecordingBackend backend) : IBlockUnitOfWorkFactory
    {
        public Task<IBlockUnitOfWork> CreateAsync(CancellationToken ct = default)
            => Task.FromResult<IBlockUnitOfWork>(new FakeUnitOfWork(backend));

        public Task<ReducerState?> GetReducerStateAsync(string reducerName, CancellationToken ct = default)
            => Task.FromResult<ReducerState?>(null);
    }

    private sealed class FakeUnitOfWork(RecordingBackend backend) : IBlockUnitOfWork
    {
        private readonly TestStore _store = new();
        private readonly Dictionary<string, Point> _intersections = [];
        private bool _marked;

        public T GetStorage<T>() where T : class
            => _store as T ?? throw new InvalidCastException(typeof(T).Name);

        public void TrackIntersection(string reducerName, Point point) => _intersections[reducerName] = point;

        public void TrackRollback(string reducerName, ulong rollbackSlot) { }

        public void MarkDataChanged() => _marked = true;

        public IReadOnlyDictionary<string, Point> TrackedIntersections => _intersections;

        public Task<bool> CommitAsync(bool deferIfEmpty = false, CancellationToken ct = default)
        {
            if (deferIfEmpty && _store.Staged.Count == 0 && !_marked)
            {
                return Task.FromResult(false); // empty batch — defer (no durable write)
            }
            backend.Committed.AddRange(_store.Staged);
            _store.Staged.Clear();
            _marked = false;
            return Task.FromResult(true);
        }

        public Task RollbackAsync(CancellationToken ct = default)
        {
            _store.Staged.Clear();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StagingReducer : IReducer
    {
        public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
        {
            uow.GetStorage<TestStore>().Staged.Add(block.Header().HeaderBody().Slot());
            return Task.CompletedTask;
        }

        public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CrashOnSlotReducer(ulong crashSlot) : IReducer
    {
        public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
            => block.Header().HeaderBody().Slot() == crashSlot
                ? throw new InvalidOperationException($"intentional crash at slot {crashSlot}")
                : Task.CompletedTask;

        public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct) => Task.CompletedTask;
    }
}
