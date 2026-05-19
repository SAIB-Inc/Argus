using Argus.Sync.Data.Models;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Tests.Infrastructure;
using Argus.Sync.Tests.Mocks;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using Argus.Sync.Example.Data;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Direct reducer test that validates blockchain sync with real Cardano data.
/// Tests rollforward processing of 5 blocks followed by progressive rollback operations.
/// Bypasses CardanoIndexWorker to directly test reducer logic and rollback semantics.
/// </summary>
public class ReducerDirectTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private readonly ITestOutputHelper _output = output;
    private TestDatabaseManager? _databaseManager;

    // Test state tracking
    private readonly Dictionary<ulong, int> _blockTxCounts = [];
    private readonly List<ulong> _blockSlots = [];
    private readonly Dictionary<ulong, BlockDetails> _blockDetails = [];

    // Test synchronization
    private int _processedBlocks;
    private bool _rollbackReceived;
    private int _rollbackCount;
    private readonly SemaphoreSlim _dbSemaphore = new(1, 1);

    public async Task InitializeAsync()
    {
        _databaseManager = new TestDatabaseManager(_output);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_databaseManager != null)
        {
            await _databaseManager.DisposeAsync();
        }
        _dbSemaphore?.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dbSemaphore?.Dispose();
            _databaseManager?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _databaseManager = null;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ReducerDirect_FiveBlocksRollForwardAndRollback_ShouldProcessCorrectly()
    {
        // Setup test environment
        (MockChainSyncProvider? mockProvider, BlockTestReducer? blockReducer, TransactionTestReducer? txReducer, IBlock[]? testBlocks) = await SetupTestEnvironmentAsync();
        if (testBlocks == null)
        {
            return; // Skip if no test data
        }

        // Analyze and log block contents
        LogBlockContentsAnalysis(testBlocks);

        // Execute rollforward phase
        Task chainSyncTask = StartChainSyncHandlerAsync(mockProvider, blockReducer, txReducer);
        await ExecuteRollForwardPhaseAsync(mockProvider, testBlocks);

        // Verify rollforward results
        await VerifyRollForwardPhaseAsync();

        // Execute rollback phase
        await ExecuteRollBackPhaseAsync(mockProvider);

        // Complete test and verify final state
        await CompleteTestAndVerifyAsync(mockProvider, chainSyncTask);
    }

    #region Setup and Configuration

    private Task<(MockChainSyncProvider, BlockTestReducer, TransactionTestReducer, IBlock[]?)> SetupTestEnvironmentAsync()
    {
        string testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        MockChainSyncProvider mockProvider = new(testDataDir);

        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            _output.WriteLine("Skipping test - no block test data found in TestData/Blocks/");
            _output.WriteLine("Run MultipleBlockCborDownloadTest first to generate test data.");
            return Task.FromResult<(MockChainSyncProvider, BlockTestReducer, TransactionTestReducer, IBlock[]?)>((mockProvider, null!, null!, null));
        }

        IDbContextFactory<TestDbContext> dbContextFactory = _databaseManager!.ServiceProvider
            .GetRequiredService<IDbContextFactory<TestDbContext>>();
        BlockTestReducer blockReducer = new();
        TransactionTestReducer txReducer = new();

        IBlock[] testBlocks = [.. mockProvider.AvailableBlocks.Take(5)];
        _output.WriteLine($"Using {testBlocks.Length} blocks for testing");

        return Task.FromResult<(MockChainSyncProvider, BlockTestReducer, TransactionTestReducer, IBlock[]?)>((mockProvider, blockReducer, txReducer, testBlocks));
    }

    #endregion

    #region Block Content Analysis

    private void LogBlockContentsAnalysis(IBlock[] testBlocks)
    {
        _output.WriteLine("\n=== Block Contents Analysis ===");

        for (int i = 0; i < testBlocks.Length; i++)
        {
            IBlock block = testBlocks[i];
            BlockInfo blockInfo = ExtractBlockInfo(block);

            _output.WriteLine($"Block {i + 1}: Slot {blockInfo.Slot}, Height {blockInfo.Height}, " +
                            $"{blockInfo.TxCount} txs, {blockInfo.Size} bytes, Hash {blockInfo.Hash[..16]}...");

            LogTransactionDetails(block, blockInfo.TxCount);
        }
    }

    private static BlockInfo ExtractBlockInfo(IBlock block)
    {
        IBlockHeaderBody header = block.Header().HeaderBody();
        return new BlockInfo
        {
            Slot = header.Slot(),
            Height = header.BlockNumber(),
            Hash = block.Header().Hash(),
            TxCount = block.TransactionBodies()?.Count() ?? 0,
            Size = CborSerializer.Serialize(block).Length
        };
    }

    private void LogTransactionDetails(IBlock block, int txCount)
    {
        if (txCount <= 0)
        {
            return;
        }

        IEnumerable<ITransactionBody> txBodies = block.TransactionBodies();
        if (txBodies == null)
        {
            return;
        }

        List<ITransactionBody> txList = [.. txBodies];
        for (int txIdx = 0; txIdx < txList.Count; txIdx++)
        {
            ITransactionBody tx = txList[txIdx];
            string txHash = tx.Hash();
            int inputCount = tx.Inputs()?.Count() ?? 0;
            int outputCount = tx.Outputs()?.Count() ?? 0;

            _output.WriteLine($"  Tx {txIdx}: Hash {txHash[..16]}..., {inputCount} inputs, {outputCount} outputs");
        }
    }

    #endregion

    #region Chain Sync Event Handling

    private Task StartChainSyncHandlerAsync(MockChainSyncProvider mockProvider,
        BlockTestReducer blockReducer, TransactionTestReducer txReducer) => Task.Run(async () =>
                                                                                 {
                                                                                     await foreach (NextResponse response in mockProvider.StartChainSyncAsync([]))
                                                                                     {
                                                                                         switch (response.Action)
                                                                                         {
                                                                                             case NextResponseAction.RollBack:
                                                                                                 await HandleRollBackAsync(response, blockReducer, txReducer);
                                                                                                 break;

                                                                                             case NextResponseAction.RollForward when response.Block != null:
                                                                                                 await HandleRollForwardAsync(response, blockReducer, txReducer);
                                                                                                 break;
                                                                                             case NextResponseAction.Await:
                                                                                                 break;
                                                                                             case NextResponseAction.RollForward:
                                                                                                 break;
                                                                                             default:
                                                                                                 break;
                                                                                         }
                                                                                     }
                                                                                 });

    private async Task HandleRollBackAsync(NextResponse response,
        BlockTestReducer blockReducer, TransactionTestReducer txReducer)
    {
        ulong rollbackSlot = response.RollbackSlot ?? 0;

        if (!_rollbackReceived)
        {
            _output.WriteLine("Received rollback to intersection point");
            _rollbackReceived = true;
            return;
        }

        // Process triggered rollback
        _output.WriteLine($"Received rollback trigger for slot {rollbackSlot}, normalized to {rollbackSlot + 1}");

        ulong normalizedRollbackSlot = rollbackSlot + 1;
        await ExecuteRollbackOperationAsync(blockReducer, txReducer, normalizedRollbackSlot);
        _rollbackCount++;

        UpdateMemoryStateForRollback(normalizedRollbackSlot, rollbackSlot);
        await VerifyRollbackStateAsync(rollbackSlot);
    }

    private async Task HandleRollForwardAsync(NextResponse response,
        BlockTestReducer blockReducer, TransactionTestReducer txReducer)
    {
        BlockInfo blockInfo = ExtractBlockInfo(response.Block!);
        List<string> txHashes = ExtractTransactionHashes(response.Block!);

        // Update tracking state
        _blockTxCounts[blockInfo.Slot] = blockInfo.TxCount;
        _blockSlots.Add(blockInfo.Slot);
        _blockDetails[blockInfo.Slot] = new BlockDetails(blockInfo.Hash, blockInfo.Height, blockInfo.TxCount, txHashes);

        _output.WriteLine($"Processing block {_processedBlocks + 1}: slot {blockInfo.Slot}, " +
                         $"{blockInfo.TxCount} transactions, hash {blockInfo.Hash[..16]}...");

        // Process with reducers — frame each reducer in its own UoW since this
        // test drives reducers directly (no worker, no per-branch sharing).
        IDbContextFactory<TestDbContext> dbcFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        await using (Argus.Sync.Reducers.IBlockUnitOfWork uow1 = new Argus.Sync.Data.Stores.EfBlockUnitOfWork<TestDbContext>(dbcFactory.CreateDbContext()))
        {
            await blockReducer.RollForwardAsync(response.Block!, uow1, CancellationToken.None);
            _ = await uow1.CommitAsync();
        }
        await using (Argus.Sync.Reducers.IBlockUnitOfWork uow2 = new Argus.Sync.Data.Stores.EfBlockUnitOfWork<TestDbContext>(dbcFactory.CreateDbContext()))
        {
            await txReducer.RollForwardAsync(response.Block!, uow2, CancellationToken.None);
            _ = await uow2.CommitAsync();
        }
        _processedBlocks++;

        await VerifyPerBlockStateAsync();
    }

    #endregion

    #region State Verification

    private async Task VerifyPerBlockStateAsync()
    {
        await _dbSemaphore.WaitAsync();
        try
        {
            int currentDbBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();
            int currentDbTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
            int expectedTotalTxs = _blockTxCounts.Values.Sum();

            Assert.Equal(_processedBlocks, currentDbBlocks);
            Assert.Equal(expectedTotalTxs, currentDbTxs);

            await VerifyMemoryDatabaseConsistencyAsync();

            _output.WriteLine($"  Per-block verification: {currentDbBlocks} blocks, {currentDbTxs} transactions in DB");
            _output.WriteLine($"  Memory-DB state consistency verified for slot {_blockSlots.Last()}");
        }
        finally
        {
            _ = _dbSemaphore.Release();
        }
    }

    private async Task VerifyRollbackStateAsync(ulong rollbackSlot)
    {
        await _dbSemaphore.WaitAsync();
        try
        {
            int currentBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();
            int currentTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
            List<ulong> remainingSlots = await GetRemainingSlots();

            ulong[] expectedRemainingSlots = [.. _blockSlots.Where(s => s <= rollbackSlot).OrderBy(s => s)];
            int expectedBlocks = expectedRemainingSlots.Length;
            int expectedTxs = expectedRemainingSlots.Sum(slot => _blockTxCounts[slot]);

            LogRollbackResults(currentBlocks, currentTxs, expectedBlocks, expectedTxs, remainingSlots);

            Assert.Equal(expectedBlocks, currentBlocks);
            Assert.Equal(expectedTxs, currentTxs);
            Assert.Equal(expectedRemainingSlots, remainingSlots.ToArray());

            await VerifyMemoryDatabaseConsistencyAsync();

            if (expectedBlocks > 0)
            {
                Assert.Contains(rollbackSlot, remainingSlots);
                _output.WriteLine($"Rollback point {rollbackSlot} preserved (Exclusive semantics)");
            }

            _output.WriteLine("Per-rollback verification passed");
            _output.WriteLine("Memory-DB state consistency verified after rollback");
        }
        finally
        {
            _ = _dbSemaphore.Release();
        }
    }

    private async Task VerifyMemoryDatabaseConsistencyAsync()
    {
        // Note: This method should be called while already holding the semaphore
        var dbBlocks = await _databaseManager!.DbContext.BlockTests
            .OrderBy(b => b.Slot)
            .Select(b => new { b.Slot, b.Hash, b.Height })
            .ToListAsync();

        var dbTxs = await _databaseManager.DbContext.TransactionTests
            .OrderBy(t => t.Slot)
            .Select(t => new { t.Slot, t.TxHash })
            .ToListAsync();

        VerifyBlockConsistency([.. dbBlocks.Cast<dynamic>()]);
        VerifyTransactionConsistency([.. dbTxs.Cast<dynamic>()]);

        Assert.Equal(dbBlocks.Count, _blockDetails.Count);
    }

    #endregion

    #region Rollforward Execution

    private async Task ExecuteRollForwardPhaseAsync(MockChainSyncProvider mockProvider, IBlock[] testBlocks)
    {
        _output.WriteLine("=== Phase 1: RollForward 5 blocks ===");

        await Task.Delay(100); // Wait for initial rollback

        for (int i = 0; i < testBlocks.Length; i++)
        {
            IBlock block = testBlocks[i];
            ulong slot = block.Header().HeaderBody().Slot();

            _output.WriteLine($"Triggering block {i + 1}/5: slot {slot}");
            await mockProvider.TriggerRollForwardAsync(slot);

            await WaitForBlockProcessing(i + 1);
        }
    }

    private async Task WaitForBlockProcessing(int targetBlockCount)
    {
        const int maxWait = 50;
        int attempts = 0;

        while (attempts < maxWait)
        {
            if (_processedBlocks >= targetBlockCount)
            {
                break;
            }

            await Task.Delay(100);
            attempts++;
        }

        if (attempts >= maxWait)
        {
            _output.WriteLine($"Warning: Timeout waiting for block {targetBlockCount} to be processed");
        }
    }

    #endregion

    #region Rollback Execution

    private async Task ExecuteRollBackPhaseAsync(MockChainSyncProvider mockProvider)
    {
        _output.WriteLine("\n=== Phase 2: RollBack one-by-one using triggers ===");

        ulong[] sortedSlots = [.. _blockSlots.OrderByDescending(s => s)];

        // Progressive rollbacks
        for (int i = 0; i < sortedSlots.Length; i++)
        {
            ulong rollbackSlot = sortedSlots[i];
            _output.WriteLine($"\n--- Triggering Rollback {i + 1}/5: targeting slot {rollbackSlot} ---");

            await mockProvider.TriggerRollBackAsync(rollbackSlot, RollBackType.Exclusive);
            await Task.Delay(300);
        }

        // Final complete rollback
        ulong finalRollbackSlot = _blockSlots.First() - 1;
        _output.WriteLine($"\n--- Triggering Final Rollback 6/6: targeting slot {finalRollbackSlot} (removes all blocks) ---");

        await mockProvider.TriggerRollBackAsync(finalRollbackSlot, RollBackType.Exclusive);
        await Task.Delay(300);
    }

    #endregion

    #region Test Completion and Verification

    private async Task VerifyRollForwardPhaseAsync()
    {
        Assert.True(_rollbackReceived, "Should have received initial rollback to intersection");
        Assert.Equal(5, _processedBlocks);

        int finalRollforwardBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();
        int finalRollforwardTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
        int totalExpectedTxs = _blockTxCounts.Values.Sum();

        Assert.Equal(5, finalRollforwardBlocks);
        Assert.Equal(totalExpectedTxs, finalRollforwardTxs);

        _output.WriteLine($"RollForward phase complete: {finalRollforwardBlocks} blocks, {finalRollforwardTxs} transactions");
        _output.WriteLine($"Block slots: [{string.Join(", ", _blockSlots)}]");
    }

    private async Task CompleteTestAndVerifyAsync(MockChainSyncProvider mockProvider, Task chainSyncTask)
    {
        mockProvider.CompleteChainSync();
        _ = await Task.WhenAny(chainSyncTask, Task.Delay(5000));

        await VerifyFinalStateAsync();
        LogTestCompletionSummary();
    }

    private async Task VerifyFinalStateAsync()
    {
        int finalBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();
        int finalTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
        List<ulong> finalSlots = await _databaseManager.DbContext.BlockTests.Select(b => b.Slot).ToListAsync();

        Assert.Equal(0, finalBlocks);
        Assert.Equal(0, finalTxs);
        Assert.Empty(finalSlots);
        Assert.Equal(6, _rollbackCount); // 5 progressive + 1 final rollback
    }

    #endregion

    #region Helper Methods

    private static List<string> ExtractTransactionHashes(IBlock block)
    {
        int txCount = block.TransactionBodies()?.Count() ?? 0;
        if (txCount == 0)
        {
            return [];
        }

        IEnumerable<ITransactionBody> txBodies = block.TransactionBodies();
        return txBodies?.Select(tx => tx.Hash()).ToList() ?? [];
    }

    private async Task ExecuteRollbackOperationAsync(BlockTestReducer blockReducer,
        TransactionTestReducer txReducer, ulong normalizedRollbackSlot)
    {
        IDbContextFactory<TestDbContext> dbcFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        await using (Argus.Sync.Reducers.IBlockUnitOfWork uow1 = new Argus.Sync.Data.Stores.EfBlockUnitOfWork<TestDbContext>(dbcFactory.CreateDbContext()))
        {
            await blockReducer.RollBackwardAsync(normalizedRollbackSlot, uow1, CancellationToken.None);
            _ = await uow1.CommitAsync();
        }
        await using (Argus.Sync.Reducers.IBlockUnitOfWork uow2 = new Argus.Sync.Data.Stores.EfBlockUnitOfWork<TestDbContext>(dbcFactory.CreateDbContext()))
        {
            await txReducer.RollBackwardAsync(normalizedRollbackSlot, uow2, CancellationToken.None);
            _ = await uow2.CommitAsync();
        }
    }

    private void UpdateMemoryStateForRollback(ulong normalizedRollbackSlot, ulong rollbackSlot)
    {
        List<ulong> slotsToRemove = [.. _blockDetails.Keys.Where(s => s >= normalizedRollbackSlot)];
        foreach (ulong slotToRemove in slotsToRemove)
        {
            _ = _blockDetails.Remove(slotToRemove);
            _output.WriteLine($"  Removed slot {slotToRemove} from memory (rollback to {rollbackSlot})");
        }
    }

    private async Task<List<ulong>> GetRemainingSlots() =>
        // Note: This method should be called while already holding the semaphore
        await _databaseManager!.DbContext.BlockTests
            .Select(b => b.Slot)
            .OrderBy(s => s)
            .ToListAsync();

    private void LogRollbackResults(int currentBlocks, int currentTxs, int expectedBlocks,
        int expectedTxs, List<ulong> remainingSlots)
    {
        _output.WriteLine($"After rollback: {currentBlocks} blocks, {currentTxs} transactions");
        _output.WriteLine($"Expected: {expectedBlocks} blocks, {expectedTxs} transactions");
        _output.WriteLine($"Remaining slots: [{string.Join(", ", remainingSlots)}]");
        _output.WriteLine($"Memory slots: [{string.Join(", ", _blockDetails.Keys.OrderBy(s => s))}]");
    }

    private void VerifyBlockConsistency(List<dynamic> dbBlocks)
    {
        foreach (dynamic dbBlock in dbBlocks)
        {
            Assert.True(_blockDetails.ContainsKey(dbBlock.Slot),
                $"Memory missing block for slot {dbBlock.Slot}");
            dynamic memoryBlock = _blockDetails[dbBlock.Slot];
            Assert.Equal(memoryBlock.Hash, dbBlock.Hash);
            Assert.Equal(memoryBlock.Height, dbBlock.Height);
        }
    }

    private void VerifyTransactionConsistency(List<dynamic> dbTxs)
    {
        Dictionary<dynamic, List<dynamic>> dbTxsBySlot = dbTxs.GroupBy(tx => tx.Slot)
            .ToDictionary(g => g.Key, g => g.Select(tx => tx.TxHash).ToList());

        foreach (KeyValuePair<ulong, BlockDetails> kvp in _blockDetails)
        {
            ulong memorySlot = kvp.Key;
            List<string> memoryTxHashes = kvp.Value.TxHashes;

            if (dbTxsBySlot.TryGetValue(memorySlot, out List<dynamic>? dbTxHashes))
            {
                Assert.Equal(memoryTxHashes.Count, dbTxHashes.Count);
                foreach (string memoryTxHash in memoryTxHashes)
                {
                    Assert.Contains(memoryTxHash, dbTxHashes);
                }
            }
            else
            {
                Assert.Empty(memoryTxHashes);
            }
        }
    }

    private void LogTestCompletionSummary()
    {
        _output.WriteLine("\nTrigger-based test completed successfully!");
        _output.WriteLine("TriggerRollForward: Triggered 5 blocks with per-block verification");
        _output.WriteLine($"TriggerRollBack: Triggered {_rollbackCount} rollbacks with per-rollback verification");
        _output.WriteLine("Final state: 0 blocks with 0 transactions (complete rollback)");
        _output.WriteLine("Trigger control: External triggers controlled all chain sync events");
    }

    #endregion

    #region Data Transfer Objects

    private record BlockInfo
    {
        public ulong Slot { get; init; }
        public ulong Height { get; init; }
        public string Hash { get; init; } = string.Empty;
        public int TxCount { get; init; }
        public int Size { get; init; }
    }

    private record BlockDetails(string Hash, ulong Height, int TxCount, List<string> TxHashes);

    #endregion
}
