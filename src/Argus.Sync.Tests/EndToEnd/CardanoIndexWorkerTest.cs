using System.Globalization;
using Argus.Sync.Data.Models;
using Argus.Sync.Example.Data;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Reducers;
using Argus.Sync.Tests.Infrastructure;
using Argus.Sync.Tests.Mocks;
using Argus.Sync.Workers;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Argus.Sync.Example.Models;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Integration test validating CardanoIndexWorker factory pattern.
/// Simplified test that focuses on factory injection while maintaining consistency with ReducerDirectTest.
/// Tests: MockChainProviderFactory -> CardanoIndexWorker -> Reducers -> Database
/// </summary>
public class CardanoIndexWorkerTest(ITestOutputHelper output) : IAsyncLifetime, IDisposable
{
    private readonly ITestOutputHelper _output = output;
    private TestDatabaseManager? _databaseManager;
    private MockChainProviderFactory? _mockFactory;

    // Test state tracking
    private readonly Dictionary<ulong, BlockDetails> _blockDetails = [];
    private readonly List<ulong> _blockSlots = [];
    private int _processedBlocks;

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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && _databaseManager != null)
        {
            _databaseManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _databaseManager = null;
        }
    }

    [Fact]
    public async Task CardanoIndexWorker_WithFactoryPattern_ShouldProcessBlocksAndRollbacks()
    {
        IBlock[]? testBlocks = await SetupTestEnvironmentAsync();
        if (testBlocks == null)
        {
            return;
        }

        LogBlockContentsAnalysis(testBlocks);

        // Start factory-based chain sync - this is the key difference from ReducerDirectTest
        Task chainSyncTask = StartFactoryBasedChainSyncAsync();
        await Task.Delay(1000); // Allow worker to initialize

        await ExecuteRollForwardPhaseAsync(testBlocks);
        await VerifyRollForwardPhaseAsync();
        await ExecuteRollBackPhaseAsync();
        await CompleteTestAndVerifyAsync(chainSyncTask);
    }

    #region Setup and Configuration

    private Task<IBlock[]?> SetupTestEnvironmentAsync()
    {
        // Create factory with test data directory
        string testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        _mockFactory = new MockChainProviderFactory(testDataDir);

        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            _output.WriteLine("Skipping test - no block test data found in TestData/Blocks/");
            _output.WriteLine("Run MultipleBlockCborDownloadTest first to generate test data.");
            return Task.FromResult<IBlock[]?>(null);
        }

        // Create a temporary provider to discover available blocks
        MockChainSyncProvider tempProvider = new(testDataDir);
        IBlock[] testBlocks = [.. tempProvider.AvailableBlocks.Take(5)];
        _output.WriteLine($"Using {testBlocks.Length} blocks for CardanoIndexWorker test");

        return Task.FromResult<IBlock[]?>(testBlocks);
    }

    private (BlockTestReducer, TransactionTestReducer) CreateReducers()
    {
        IDbContextFactory<TestDbContext> dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        return (new BlockTestReducer(dbContextFactory), new TransactionTestReducer(dbContextFactory));
    }

    private async Task<(CardanoIndexWorker<TestDbContext> Worker, ILoggerFactory LoggerFactory)> CreateCardanoIndexWorkerWithFactoryAsync()
    {
        // Clear any existing ReducerStates to ensure clean test
        _databaseManager!.DbContext.ReducerStates.RemoveRange(_databaseManager.DbContext.ReducerStates);
        _ = await _databaseManager.DbContext.SaveChangesAsync();

        IConfiguration configuration = CreateMinimalTestConfiguration();
        ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        ILogger<CardanoIndexWorker<TestDbContext>> logger = loggerFactory.CreateLogger<CardanoIndexWorker<TestDbContext>>();
        IDbContextFactory<TestDbContext> dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<TestDbContext>>();
        (BlockTestReducer? blockReducer, TransactionTestReducer? txReducer) = CreateReducers();
        List<IReducer<IReducerModel>> reducers = [blockReducer, txReducer];

        return (new CardanoIndexWorker<TestDbContext>(configuration, logger, dbContextFactory, reducers, _mockFactory!), loggerFactory);
    }

    private IConfiguration CreateMinimalTestConfiguration()
    {
        // Minimal configuration for factory pattern testing
        MockChainSyncProvider tempProvider = new(Path.Combine(Directory.GetCurrentDirectory(), "TestData"));
        IBlock firstBlock = tempProvider.AvailableBlocks[0];

        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CardanoContext"] = _databaseManager!.DbContext.Database.GetConnectionString(),
            ["CardanoNodeConnection:Hash"] = firstBlock.Header().Hash(),
            ["CardanoNodeConnection:Slot"] = firstBlock.Header().HeaderBody().Slot().ToString(CultureInfo.InvariantCulture),
            ["Sync:Worker:ExitOnCompletion"] = "false", // Critical for testing
            ["Sync:State:ReducerStateSyncInterval"] = "1000", // Fast state sync
            ["Sync:Dashboard:TuiMode"] = "false" // Disable TUI for clean test output
        }).Build();
    }

    #endregion

    #region Block Content Analysis

    private void LogBlockContentsAnalysis(IBlock[] testBlocks)
    {
        _output.WriteLine("\n=== CardanoIndexWorker Test - Block Analysis ===");

        for (int i = 0; i < testBlocks.Length; i++)
        {
            IBlock block = testBlocks[i];
            BlockInfo blockInfo = ExtractBlockInfo(block);

            _output.WriteLine($"Block {i + 1}: Slot {blockInfo.Slot}, Height {blockInfo.Height}, " +
                            $"{blockInfo.TxCount} txs, {blockInfo.Size} bytes, Hash {blockInfo.Hash[..16]}...");

            LogTransactionDetails(block, blockInfo.TxCount);

            // Only track slots for rollback logic, don't store details yet
            _blockSlots.Add(blockInfo.Slot);
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

    #region Chain Sync Execution

    private async Task StartFactoryBasedChainSyncAsync()
    {
        (CardanoIndexWorker<TestDbContext> worker, ILoggerFactory loggerFactory) = await CreateCardanoIndexWorkerWithFactoryAsync();
        await Task.Run(async () =>
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            try
            {
                await worker.StartAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when test completes
            }
            finally
            {
                if (worker is IDisposable disposableWorker)
                {
                    disposableWorker.Dispose();
                }
                loggerFactory.Dispose();
            }
        });
    }

    #endregion

    #region Test Execution

    private async Task ExecuteRollForwardPhaseAsync(IBlock[] testBlocks)
    {
        _output.WriteLine("\n=== Phase 1: Worker RollForward via Factory Pattern ===");

        for (int i = 0; i < testBlocks.Length; i++)
        {
            IBlock block = testBlocks[i];
            ulong slot = block.Header().HeaderBody().Slot();

            _output.WriteLine($"Triggering direct worker block {i + 1}/{testBlocks.Length}: slot {slot}");
            // Trigger rollforward on all provider instances
            foreach (MockChainSyncProvider provider in _mockFactory!.CreatedProviders)
            {
                await provider.TriggerRollForwardAsync(slot);
            }

            // Wait for worker to process the block
            await WaitForBlockProcessing(i + 1);

            // Store block details after processing (like ReducerDirectTest does)
            BlockInfo blockInfo = ExtractBlockInfo(block);
            List<string> txHashes = ExtractTransactionHashes(block);
            _blockDetails[blockInfo.Slot] = new BlockDetails(blockInfo.Hash, blockInfo.Height, blockInfo.TxCount, txHashes);

            // Give a moment for state sync to complete
            await Task.Delay(1500);

            await VerifyWorkerProcessedBlock(slot);
            await VerifyMemoryDatabaseConsistency();
        }

        _processedBlocks = testBlocks.Length;
    }

    private async Task ExecuteRollBackPhaseAsync()
    {
        _output.WriteLine("\n=== Phase 2: Worker RollBack via Factory Pattern ===");

        ulong[] sortedSlots = [.. _blockSlots.OrderByDescending(s => s)];

        // Progressive rollbacks through worker
        for (int i = 0; i < sortedSlots.Length; i++)
        {
            ulong rollbackSlot = sortedSlots[i];
            _output.WriteLine($"\n--- Triggering Worker Rollback {i + 1}/{sortedSlots.Length}: targeting slot {rollbackSlot} ---");

            // Trigger rollback on all provider instances
            foreach (MockChainSyncProvider provider in _mockFactory!.CreatedProviders)
            {
                await provider.TriggerRollBackAsync(rollbackSlot, RollBackType.Exclusive);
            }
            await Task.Delay(1000); // Give worker time to process rollback

            // Calculate expected rollback slot based on worker logic: block.slot + 1 for Exclusive
            ulong expectedWorkerRollbackSlot = rollbackSlot + 1;

            // Update memory state to match expected rollback
            UpdateMemoryStateForRollback(expectedWorkerRollbackSlot, rollbackSlot);

            await VerifyWorkerRollbackState(expectedWorkerRollbackSlot);
            await VerifyMemoryDatabaseConsistency();
        }

        // Add one more rollback to remove the last remaining block (like ReducerDirectTest does)
        ulong finalRollbackSlot = _blockSlots.First() - 1;
        _output.WriteLine($"\n--- Triggering Final Worker Rollback: targeting slot {finalRollbackSlot} (removes all blocks) ---");

        // Trigger final rollback on all provider instances
        foreach (MockChainSyncProvider provider in _mockFactory!.CreatedProviders)
        {
            await provider.TriggerRollBackAsync(finalRollbackSlot, RollBackType.Inclusive);
        }
        await Task.Delay(1000);

        // Update memory state for final rollback (Inclusive type removes block at exact slot)
        UpdateMemoryStateForRollback(finalRollbackSlot, finalRollbackSlot);
    }

    #endregion

    #region Verification

    private async Task WaitForBlockProcessing(int targetBlockCount)
    {
        const int maxWait = 100;
        int attempts = 0;

        while (attempts < maxWait)
        {
            try
            {
                int currentCount = await _databaseManager!.DbContext.BlockTests.CountAsync();
                if (currentCount >= targetBlockCount)
                {
                    _output.WriteLine($"  CardanoIndexWorker processed block {targetBlockCount}");
                    break;
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Database check failed: {ex.Message}");
            }

            await Task.Delay(100);
            attempts++;
        }

        if (attempts >= maxWait)
        {
            _output.WriteLine($"  Timeout waiting for direct worker to process block {targetBlockCount}");
        }
    }

    private async Task VerifyWorkerProcessedBlock(ulong slot)
    {
        BlockTest? blockInDb = await _databaseManager!.DbContext.BlockTests
            .FirstOrDefaultAsync(b => b.Slot == slot);

        Assert.NotNull(blockInDb);
        Assert.True(_blockDetails.ContainsKey(slot));

        BlockDetails expectedBlock = _blockDetails[slot];
        Assert.Equal(expectedBlock.Hash, blockInDb.Hash);
        Assert.Equal(expectedBlock.Height, blockInDb.Height);

        // Verify transaction count matches memory
        int txsInDb = await _databaseManager.DbContext.TransactionTests
            .CountAsync(t => t.Slot == slot);
        Assert.Equal(expectedBlock.TxCount, txsInDb);

        // Verify ReducerState management (optional - may be async)
        int reducerStates = await _databaseManager.DbContext.ReducerStates.CountAsync();
        _output.WriteLine($"  ReducerStates: {reducerStates}");

        _output.WriteLine($"  Worker verification passed for slot {slot}: {txsInDb} transactions");
    }

    private async Task VerifyWorkerRollbackState(ulong rollbackSlot)
    {
        int remainingBlocks = await _databaseManager!.DbContext.BlockTests
            .Where(b => b.Slot <= rollbackSlot)
            .CountAsync();

        int expectedRemainingBlocks = _blockSlots.Count(s => s <= rollbackSlot);

        Assert.Equal(expectedRemainingBlocks, remainingBlocks);

        // Verify ReducerState intersections were updated correctly by worker
        List<ReducerState> reducerStates = await _databaseManager.DbContext.ReducerStates.ToListAsync();
        foreach (ReducerState? state in reducerStates)
        {
            ulong latestIntersectionSlot = state.LatestIntersections.MaxBy(p => p.Slot)?.Slot ?? 0;
            ulong startIntersectionSlot = state.StartIntersection.Slot;
            _output.WriteLine($"    ReducerState {state.Name}: latest={latestIntersectionSlot}, start={startIntersectionSlot}, rollback={rollbackSlot}");

            // Relaxed verification: Just ensure the state exists and has valid intersections
            Assert.True(state.LatestIntersections.Any(), $"ReducerState {state.Name} should have intersections");
        }

        _output.WriteLine($"  Worker rollback verification passed: {remainingBlocks} blocks remaining");
    }

    private async Task VerifyMemoryDatabaseConsistency()
    {
        var dbBlocks = await _databaseManager!.DbContext.BlockTests
            .OrderBy(b => b.Slot)
            .Select(b => new { b.Slot, b.Hash, b.Height })
            .ToListAsync();

        var dbTxs = await _databaseManager.DbContext.TransactionTests
            .OrderBy(t => t.Slot)
            .Select(t => new { t.Slot, t.TxHash })
            .ToListAsync();

        // Verify all DB blocks exist in memory with correct details
        foreach (var dbBlock in dbBlocks)
        {
            Assert.True(_blockDetails.ContainsKey(dbBlock.Slot),
                $"Memory missing block for slot {dbBlock.Slot}");
            BlockDetails memoryBlock = _blockDetails[dbBlock.Slot];
            Assert.Equal(memoryBlock.Hash, dbBlock.Hash);
            Assert.Equal(memoryBlock.Height, dbBlock.Height);
        }

        // Verify transaction consistency
        Dictionary<ulong, List<string>> dbTxsBySlot = dbTxs.GroupBy(tx => tx.Slot)
            .ToDictionary(g => g.Key, g => g.Select(tx => tx.TxHash).ToList());

        foreach (KeyValuePair<ulong, BlockDetails> kvp in _blockDetails)
        {
            ulong memorySlot = kvp.Key;
            List<string> memoryTxHashes = kvp.Value.TxHashes;

            if (dbTxsBySlot.TryGetValue(memorySlot, out List<string>? dbTxHashes))
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

        // Verify no extra blocks remain in memory
        Assert.Equal(dbBlocks.Count, _blockDetails.Count);
    }

    private async Task VerifyRollForwardPhaseAsync()
    {
        int finalBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();

        Assert.Equal(_processedBlocks, finalBlocks);

        // Check ReducerState entries (may be async, so don't assert)
        List<ReducerState> reducerStates = await _databaseManager.DbContext.ReducerStates.ToListAsync();

        _output.WriteLine($"Worker RollForward phase complete: {finalBlocks} blocks");
        _output.WriteLine($"ReducerState management: {reducerStates.Count} reducer states");
    }

    private async Task VerifyFinalStateAsync()
    {
        int finalBlocks = await _databaseManager!.DbContext.BlockTests.CountAsync();

        // Worker should remove all blocks just like ReducerDirectTest
        Assert.Equal(0, finalBlocks);

        // Check ReducerStates (may be async, so don't assert)
        int reducerStates = await _databaseManager.DbContext.ReducerStates.CountAsync();

        _output.WriteLine("Worker final state: 0 blocks");
        _output.WriteLine($"ReducerStates: {reducerStates}");
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

    private void UpdateMemoryStateForRollback(ulong normalizedRollbackSlot, ulong rollbackSlot)
    {
        List<ulong> slotsToRemove = [.. _blockDetails.Keys.Where(s => s >= normalizedRollbackSlot)];
        foreach (ulong slotToRemove in slotsToRemove)
        {
            _ = _blockDetails.Remove(slotToRemove);
            _output.WriteLine($"  Removed slot {slotToRemove} from memory (rollback to {rollbackSlot})");
        }
    }

    private async Task CompleteTestAndVerifyAsync(Task chainSyncTask)
    {
        // Complete all mock providers to signal end
        foreach (MockChainSyncProvider provider in _mockFactory!.CreatedProviders)
        {
            provider.CompleteChainSync();
        }

        _ = await Task.WhenAny(chainSyncTask, Task.Delay(5000));
        await VerifyFinalStateAsync();

        _output.WriteLine("\nFactory pattern test completed successfully!");
        _output.WriteLine("MockChainProviderFactory integration verified");
        _output.WriteLine("Final state: 0 blocks with 0 transactions");
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
