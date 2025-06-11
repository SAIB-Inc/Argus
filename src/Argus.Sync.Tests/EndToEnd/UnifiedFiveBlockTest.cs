using Argus.Sync.Data.Models;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Tests.Infrastructure;
using Argus.Sync.Tests.Mocks;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Comprehensive end-to-end test that validates blockchain sync with real Cardano data.
/// Tests rollforward processing of 5 blocks followed by progressive rollback operations.
/// Includes detailed block content analysis and memory-database state verification.
/// </summary>
public class UnifiedFiveBlockTest : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TestDatabaseManager? _databaseManager;

    // Test state tracking
    private readonly Dictionary<ulong, int> _blockTxCounts = new();
    private readonly List<ulong> _blockSlots = new();
    private readonly Dictionary<ulong, BlockDetails> _blockDetails = new();
    
    // Test synchronization
    private int _processedBlocks;
    private bool _rollbackReceived;
    private int _rollbackCount;

    public UnifiedFiveBlockTest(ITestOutputHelper output)
    {
        _output = output;
    }

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
    }

    [Fact]
    public async Task FiveBlocks_RollForwardThenRollBack_ShouldVerifyPerBlockAndPerRollback()
    {
        // Setup test environment
        var (mockProvider, blockReducer, txReducer, testBlocks) = await SetupTestEnvironmentAsync();
        if (testBlocks == null) return; // Skip if no test data

        // Analyze and log block contents
        LogBlockContentsAnalysis(testBlocks);

        // Execute rollforward phase
        var chainSyncTask = StartChainSyncHandlerAsync(mockProvider, blockReducer, txReducer);
        await ExecuteRollForwardPhaseAsync(mockProvider, testBlocks);

        // Verify rollforward results
        await VerifyRollForwardPhaseAsync();

        // Execute rollback phase
        await ExecuteRollBackPhaseAsync(mockProvider);

        // Complete test and verify final state
        await CompleteTestAndVerifyAsync(mockProvider, chainSyncTask);
    }

    #region Setup and Configuration

    private Task<(MockChainSyncProvider, BlockTestReducer, TransactionTestReducer, Block[]?)> SetupTestEnvironmentAsync()
    {
        var testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        var mockProvider = new MockChainSyncProvider(testDataDir);
        
        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            _output.WriteLine("Skipping test - no block test data found in TestData/Blocks/");
            _output.WriteLine("Run MultipleBlockCborDownloadTest first to generate test data.");
            return Task.FromResult<(MockChainSyncProvider, BlockTestReducer, TransactionTestReducer, Block[]?)>((mockProvider, null!, null!, null));
        }
        
        var dbContextFactory = _databaseManager!.ServiceProvider
            .GetRequiredService<IDbContextFactory<Argus.Sync.Example.Data.TestDbContext>>();
        var blockReducer = new BlockTestReducer(dbContextFactory);
        var txReducer = new TransactionTestReducer(dbContextFactory);
        
        var testBlocks = mockProvider.AvailableBlocks.Take(5).ToArray();
        _output.WriteLine($"Using {testBlocks.Length} blocks for testing");
        
        return Task.FromResult<(MockChainSyncProvider, BlockTestReducer, TransactionTestReducer, Block[]?)>((mockProvider, blockReducer, txReducer, testBlocks));
    }

    #endregion

    #region Block Content Analysis

    private void LogBlockContentsAnalysis(Block[] testBlocks)
    {
        _output.WriteLine("\n=== Block Contents Analysis ===");
        
        for (int i = 0; i < testBlocks.Length; i++)
        {
            var block = testBlocks[i];
            var blockInfo = ExtractBlockInfo(block);
            
            _output.WriteLine($"Block {i + 1}: Slot {blockInfo.Slot}, Height {blockInfo.Height}, " +
                            $"{blockInfo.TxCount} txs, {blockInfo.Size} bytes, Hash {blockInfo.Hash[..16]}...");
            
            LogTransactionDetails(block, blockInfo.TxCount);
        }
    }

    private static BlockInfo ExtractBlockInfo(Block block)
    {
        var header = block.Header().HeaderBody();
        return new BlockInfo
        {
            Slot = header.Slot(),
            Height = header.BlockNumber(),
            Hash = block.Header().Hash(),
            TxCount = block.TransactionBodies()?.Count() ?? 0,
            Size = CborSerializer.Serialize(block).Length
        };
    }

    private void LogTransactionDetails(Block block, int txCount)
    {
        if (txCount <= 0) return;

        var txBodies = block.TransactionBodies();
        if (txBodies == null) return;

        var txList = txBodies.ToList();
        for (int txIdx = 0; txIdx < txList.Count; txIdx++)
        {
            var tx = txList[txIdx];
            var txHash = tx.Hash();
            var inputCount = tx.Inputs()?.Count() ?? 0;
            var outputCount = tx.Outputs()?.Count() ?? 0;
            
            _output.WriteLine($"  Tx {txIdx}: Hash {txHash[..16]}..., {inputCount} inputs, {outputCount} outputs");
        }
    }

    #endregion

    #region Chain Sync Event Handling

    private Task StartChainSyncHandlerAsync(MockChainSyncProvider mockProvider, 
        BlockTestReducer blockReducer, TransactionTestReducer txReducer)
    {
        return Task.Run(async () =>
        {
            await foreach (var response in mockProvider.StartChainSyncAsync([]))
            {
                switch (response.Action)
                {
                    case NextResponseAction.RollBack when response.Block != null:
                        await HandleRollBackAsync(response, mockProvider, blockReducer, txReducer);
                        break;
                        
                    case NextResponseAction.RollForward when response.Block != null:
                        await HandleRollForwardAsync(response, blockReducer, txReducer);
                        break;
                }
            }
        });
    }

    private async Task HandleRollBackAsync(NextResponse response, MockChainSyncProvider mockProvider,
        BlockTestReducer blockReducer, TransactionTestReducer txReducer)
    {
        var rollbackSlot = mockProvider.GetActualRollbackSlot(response);
        
        if (!_rollbackReceived)
        {
            _output.WriteLine("Received rollback to intersection point");
            _rollbackReceived = true;
            return;
        }

        // Process triggered rollback
        _output.WriteLine($"Received rollback trigger for slot {rollbackSlot}, normalized to {rollbackSlot + 1}");
        
        var normalizedRollbackSlot = rollbackSlot + 1;
        await ExecuteRollbackOperationAsync(blockReducer, txReducer, normalizedRollbackSlot);
        _rollbackCount++;
        
        UpdateMemoryStateForRollback(normalizedRollbackSlot, rollbackSlot);
        await VerifyRollbackStateAsync(rollbackSlot);
    }

    private async Task HandleRollForwardAsync(NextResponse response, 
        BlockTestReducer blockReducer, TransactionTestReducer txReducer)
    {
        var blockInfo = ExtractBlockInfo(response.Block);
        var txHashes = ExtractTransactionHashes(response.Block);
        
        // Update tracking state
        _blockTxCounts[blockInfo.Slot] = blockInfo.TxCount;
        _blockSlots.Add(blockInfo.Slot);
        _blockDetails[blockInfo.Slot] = new BlockDetails(blockInfo.Hash, blockInfo.Height, blockInfo.TxCount, txHashes);
        
        _output.WriteLine($"Processing block {_processedBlocks + 1}: slot {blockInfo.Slot}, " +
                         $"{blockInfo.TxCount} transactions, hash {blockInfo.Hash[..16]}...");
        
        // Process with reducers
        await blockReducer.RollForwardAsync(response.Block);
        await txReducer.RollForwardAsync(response.Block);
        _processedBlocks++;
        
        await VerifyPerBlockStateAsync();
    }

    #endregion

    #region State Verification

    private async Task VerifyPerBlockStateAsync()
    {
        var currentDbBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();
        var currentDbTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
        var expectedTotalTxs = _blockTxCounts.Values.Sum();
        
        Assert.Equal(_processedBlocks, currentDbBlocks);
        Assert.Equal(expectedTotalTxs, currentDbTxs);
        
        await VerifyMemoryDatabaseConsistencyAsync();
        
        _output.WriteLine($"  ✅ Per-block verification: {currentDbBlocks} blocks, {currentDbTxs} transactions in DB");
        _output.WriteLine($"  ✅ Memory-DB state consistency verified for slot {_blockSlots.Last()}");
    }

    private async Task VerifyRollbackStateAsync(ulong rollbackSlot)
    {
        var currentBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();
        var currentTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
        var remainingSlots = await GetRemainingSlots();
        
        var expectedRemainingSlots = _blockSlots.Where(s => s <= rollbackSlot).OrderBy(s => s).ToArray();
        var expectedBlocks = expectedRemainingSlots.Length;
        var expectedTxs = expectedRemainingSlots.Sum(slot => _blockTxCounts[slot]);
        
        LogRollbackResults(currentBlocks, currentTxs, expectedBlocks, expectedTxs, remainingSlots);
        
        Assert.Equal(expectedBlocks, currentBlocks);
        Assert.Equal(expectedTxs, currentTxs);
        Assert.Equal(expectedRemainingSlots, remainingSlots.ToArray());
        
        await VerifyMemoryDatabaseConsistencyAsync();
        
        if (expectedBlocks > 0)
        {
            Assert.Contains(rollbackSlot, remainingSlots);
            _output.WriteLine($"✅ Rollback point {rollbackSlot} preserved (Exclusive semantics)");
        }
        
        _output.WriteLine("✅ Per-rollback verification passed");
        _output.WriteLine("✅ Memory-DB state consistency verified after rollback");
    }

    private async Task VerifyMemoryDatabaseConsistencyAsync()
    {
        var dbBlocks = await _databaseManager!.DbContext.BlockTests
            .OrderBy(b => b.Slot)
            .Select(b => new { b.Slot, b.Hash, b.Height })
            .ToListAsync();
        
        var dbTxs = await _databaseManager.DbContext.TransactionTests
            .OrderBy(t => t.Slot)
            .Select(t => new { t.Slot, t.TxHash })
            .ToListAsync();
        
        VerifyBlockConsistency(dbBlocks.Cast<dynamic>().ToList());
        VerifyTransactionConsistency(dbTxs.Cast<dynamic>().ToList());
        
        Assert.Equal(dbBlocks.Count, _blockDetails.Count);
    }

    #endregion

    #region Rollforward Execution

    private async Task ExecuteRollForwardPhaseAsync(MockChainSyncProvider mockProvider, Block[] testBlocks)
    {
        _output.WriteLine("=== Phase 1: RollForward 5 blocks ===");
        
        await Task.Delay(100); // Wait for initial rollback
        
        for (int i = 0; i < testBlocks.Length; i++)
        {
            var block = testBlocks[i];
            var slot = block.Header().HeaderBody().Slot();
            
            _output.WriteLine($"Triggering block {i + 1}/5: slot {slot}");
            await mockProvider.TriggerRollForwardAsync(slot);
            
            await WaitForBlockProcessing(i + 1);
        }
    }

    private async Task WaitForBlockProcessing(int targetBlockCount)
    {
        const int maxWait = 50;
        var attempts = 0;
        
        while (attempts < maxWait)
        {
            try
            {
                var currentCount = await _databaseManager!.DbContext.BlockTests.CountAsync();
                if (currentCount >= targetBlockCount) break;
            }
            catch
            {
                // Database might be temporarily busy, continue waiting
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
        
        var sortedSlots = _blockSlots.OrderByDescending(s => s).ToArray();
        
        // Progressive rollbacks
        for (int i = 0; i < sortedSlots.Length; i++)
        {
            var rollbackSlot = sortedSlots[i];
            _output.WriteLine($"\n--- Triggering Rollback {i + 1}/5: targeting slot {rollbackSlot} ---");
            
            await mockProvider.TriggerRollBackAsync(rollbackSlot, RollBackType.Exclusive);
            await Task.Delay(300);
        }
        
        // Final complete rollback
        var finalRollbackSlot = _blockSlots.First() - 1;
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
        
        var finalRollforwardBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();
        var finalRollforwardTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
        var totalExpectedTxs = _blockTxCounts.Values.Sum();
        
        Assert.Equal(5, finalRollforwardBlocks);
        Assert.Equal(totalExpectedTxs, finalRollforwardTxs);
        
        _output.WriteLine($"✅ RollForward phase complete: {finalRollforwardBlocks} blocks, {finalRollforwardTxs} transactions");
        _output.WriteLine($"✅ Block slots: [{string.Join(", ", _blockSlots)}]");
    }

    private async Task CompleteTestAndVerifyAsync(MockChainSyncProvider mockProvider, Task chainSyncTask)
    {
        mockProvider.CompleteChainSync();
        await Task.WhenAny(chainSyncTask, Task.Delay(5000));
        
        await VerifyFinalStateAsync();
        LogTestCompletionSummary();
    }

    private async Task VerifyFinalStateAsync()
    {
        var finalBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();
        var finalTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
        var finalSlots = await _databaseManager.DbContext.BlockTests.Select(b => b.Slot).ToListAsync();
        
        Assert.Equal(0, finalBlocks);
        Assert.Equal(0, finalTxs);
        Assert.Empty(finalSlots);
        Assert.Equal(6, _rollbackCount); // 5 progressive + 1 final rollback
    }

    #endregion

    #region Helper Methods

    private static List<string> ExtractTransactionHashes(Block block)
    {
        var txCount = block.TransactionBodies()?.Count() ?? 0;
        if (txCount == 0) return new List<string>();

        var txBodies = block.TransactionBodies();
        return txBodies?.Select(tx => tx.Hash()).ToList() ?? new List<string>();
    }

    private async Task ExecuteRollbackOperationAsync(BlockTestReducer blockReducer, 
        TransactionTestReducer txReducer, ulong normalizedRollbackSlot)
    {
        await blockReducer.RollBackwardAsync(normalizedRollbackSlot);
        await txReducer.RollBackwardAsync(normalizedRollbackSlot);
    }

    private void UpdateMemoryStateForRollback(ulong normalizedRollbackSlot, ulong rollbackSlot)
    {
        var slotsToRemove = _blockDetails.Keys.Where(s => s >= normalizedRollbackSlot).ToList();
        foreach (var slotToRemove in slotsToRemove)
        {
            _blockDetails.Remove(slotToRemove);
            _output.WriteLine($"  Removed slot {slotToRemove} from memory (rollback to {rollbackSlot})");
        }
    }

    private async Task<List<ulong>> GetRemainingSlots()
    {
        return await _databaseManager!.DbContext.BlockTests
            .Select(b => b.Slot)
            .OrderBy(s => s)
            .ToListAsync();
    }

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
        foreach (var dbBlock in dbBlocks)
        {
            Assert.True(_blockDetails.ContainsKey(dbBlock.Slot), 
                $"Memory missing block for slot {dbBlock.Slot}");
            var memoryBlock = _blockDetails[dbBlock.Slot];
            Assert.Equal(memoryBlock.Hash, dbBlock.Hash);
            Assert.Equal(memoryBlock.Height, dbBlock.Height);
        }
    }

    private void VerifyTransactionConsistency(List<dynamic> dbTxs)
    {
        var dbTxsBySlot = dbTxs.GroupBy(tx => tx.Slot)
            .ToDictionary(g => g.Key, g => g.Select(tx => tx.TxHash).ToList());
        
        foreach (var kvp in _blockDetails)
        {
            var memorySlot = kvp.Key;
            var memoryTxHashes = kvp.Value.TxHashes;
            
            if (dbTxsBySlot.TryGetValue(memorySlot, out var dbTxHashes))
            {
                Assert.Equal(memoryTxHashes.Count, dbTxHashes.Count);
                foreach (var memoryTxHash in memoryTxHashes)
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
        _output.WriteLine("\n✅ Trigger-based test completed successfully!");
        _output.WriteLine("✅ TriggerRollForward: Triggered 5 blocks with per-block verification");
        _output.WriteLine($"✅ TriggerRollBack: Triggered {_rollbackCount} rollbacks with per-rollback verification");
        _output.WriteLine("✅ Final state: 0 blocks with 0 transactions (complete rollback)");
        _output.WriteLine("✅ Trigger control: External triggers controlled all chain sync events");
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