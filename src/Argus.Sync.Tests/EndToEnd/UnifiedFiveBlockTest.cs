using Argus.Sync.Data.Models;
using Argus.Sync.Example.Reducers;
using Argus.Sync.Tests.Infrastructure;
using Argus.Sync.Tests.Mocks;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Unified test: RollForward 5 blocks with per-block verification, then RollBack with per-rollback verification.
/// Uses proper ICardanoChainProvider interface without custom methods.
/// </summary>
public class UnifiedFiveBlockTest : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private TestDatabaseManager? _databaseManager;

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
        // Arrange - Create mock provider using only ICardanoChainProvider interface
        var testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData");
        var mockProvider = new MockChainSyncProvider(testDataDir);
        
        if (!Directory.Exists(Path.Combine(testDataDir, "Blocks")))
        {
            _output.WriteLine("Skipping test - no block test data found in TestData/Blocks/");
            _output.WriteLine("Run MultipleBlockCborDownloadTest first to generate test data.");
            return;
        }
        
        var dbContextFactory = _databaseManager!.ServiceProvider.GetRequiredService<IDbContextFactory<Argus.Sync.Example.Data.TestDbContext>>();
        var blockReducer = new BlockTestReducer(dbContextFactory);
        var txReducer = new TransactionTestReducer(dbContextFactory);
        
        // Phase 1: RollForward 5 blocks with per-block verification
        _output.WriteLine("=== Phase 1: RollForward 5 blocks ===");
        
        var processedBlocks = 0;
        var rollbackReceived = false;
        var blockTxCounts = new Dictionary<ulong, int>(); // Track tx count per block
        var blockSlots = new List<ulong>(); // Track processed slots in order
        
        await foreach (var response in mockProvider.StartChainSyncAsync([]))
        {
            switch (response.Action)
            {
                case NextResponseAction.RollBack:
                    _output.WriteLine("Received rollback to intersection point");
                    rollbackReceived = true;
                    break;
                    
                case NextResponseAction.RollForward when response.Block != null:
                    var slot = response.Block.Header().HeaderBody().Slot();
                    var txCount = response.Block.TransactionBodies()?.Count() ?? 0;
                    var hash = response.Block.Header().Hash();
                    
                    // Store block info for rollback phase
                    blockTxCounts[slot] = txCount;
                    blockSlots.Add(slot);
                    
                    _output.WriteLine($"Processing block {processedBlocks + 1}/5: slot {slot}, {txCount} transactions, hash {hash[..16]}...");
                    
                    // Process block with reducers
                    await blockReducer.RollForwardAsync(response.Block);
                    await txReducer.RollForwardAsync(response.Block);
                    processedBlocks++;
                    
                    // Verify per-block: check database after each block
                    var currentDbBlocks = await _databaseManager.DbContext.BlockTests.CountAsync();
                    var currentDbTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
                    var expectedTotalTxs = blockTxCounts.Values.Sum();
                    
                    Assert.Equal(processedBlocks, currentDbBlocks);
                    Assert.Equal(expectedTotalTxs, currentDbTxs);
                    
                    _output.WriteLine($"  ✅ Per-block verification: {currentDbBlocks} blocks, {currentDbTxs} transactions in DB");
                    
                    break;
            }
            
            // Stop after 5 blocks (consumer controls consumption)
            if (processedBlocks >= 5) break;
        }
        
        // Verify rollforward phase
        Assert.True(rollbackReceived, "Should have received initial rollback to intersection");
        Assert.Equal(5, processedBlocks);
        
        var finalRollforwardBlocks = await _databaseManager.DbContext.BlockTests.CountAsync();
        var finalRollforwardTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
        var totalExpectedTxs = blockTxCounts.Values.Sum();
        
        Assert.Equal(5, finalRollforwardBlocks);
        Assert.Equal(totalExpectedTxs, finalRollforwardTxs);
        
        _output.WriteLine($"✅ RollForward phase complete: {finalRollforwardBlocks} blocks, {finalRollforwardTxs} transactions");
        _output.WriteLine($"✅ Block slots: [{string.Join(", ", blockSlots)}]");
        
        // Phase 2: RollBack one by one with per-rollback verification
        _output.WriteLine("\n=== Phase 2: RollBack one-by-one ===");
        
        // Roll back from highest slot to lowest (exclusive semantics)
        var sortedSlots = blockSlots.OrderByDescending(s => s).ToArray();
        
        for (int i = 0; i < sortedSlots.Length; i++)
        {
            var rollbackSlot = sortedSlots[i];
            
            // Apply CardanoIndexWorker normalization for Exclusive rollback
            var normalizedRollbackSlot = rollbackSlot + 1;
            
            _output.WriteLine($"\n--- Rollback {i + 1}/5: targeting slot {rollbackSlot} ---");
            _output.WriteLine($"Normalized rollback slot: {normalizedRollbackSlot} (removes >= {normalizedRollbackSlot})");
            
            await blockReducer.RollBackwardAsync(normalizedRollbackSlot);
            await txReducer.RollBackwardAsync(normalizedRollbackSlot);
            
            // Verify per-rollback: check counts after each rollback
            var currentBlocks = await _databaseManager.DbContext.BlockTests.CountAsync();
            var currentTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
            var remainingSlots = await _databaseManager.DbContext.BlockTests
                .Select(b => b.Slot)
                .OrderBy(s => s)
                .ToListAsync();
            
            // Expected: blocks with slot <= rollbackSlot should remain (exclusive semantics)
            var expectedRemainingSlots = blockSlots.Where(s => s <= rollbackSlot).OrderBy(s => s).ToArray();
            var expectedBlocks = expectedRemainingSlots.Length;
            var expectedTxs = expectedRemainingSlots.Sum(slot => blockTxCounts[slot]);
            
            _output.WriteLine($"After rollback: {currentBlocks} blocks, {currentTxs} transactions");
            _output.WriteLine($"Expected: {expectedBlocks} blocks, {expectedTxs} transactions");
            _output.WriteLine($"Remaining slots: [{string.Join(", ", remainingSlots)}]");
            
            Assert.Equal(expectedBlocks, currentBlocks);
            Assert.Equal(expectedTxs, currentTxs);
            Assert.Equal(expectedRemainingSlots, remainingSlots.ToArray());
            
            // Verify rollback point preserved (exclusive semantics)
            if (expectedBlocks > 0)
            {
                Assert.Contains(rollbackSlot, remainingSlots);
                _output.WriteLine($"✅ Rollback point {rollbackSlot} preserved (Exclusive semantics)");
            }
            
            _output.WriteLine($"✅ Per-rollback verification passed");
        }
        
        // Final verification: should have 1 block remaining (first block)
        var finalBlocks = await _databaseManager.DbContext.BlockTests.CountAsync();
        var finalTxs = await _databaseManager.DbContext.TransactionTests.CountAsync();
        var finalSlots = await _databaseManager.DbContext.BlockTests.Select(b => b.Slot).ToListAsync();
        
        Assert.Equal(1, finalBlocks);
        Assert.Equal(blockTxCounts[blockSlots.First()], finalTxs);
        Assert.Contains(blockSlots.First(), finalSlots);
        
        _output.WriteLine($"\n✅ Unified test completed successfully!");
        _output.WriteLine($"✅ RollForward: Processed 5 blocks with per-block verification");
        _output.WriteLine($"✅ RollBack: Rolled back 4 blocks with per-rollback verification");
        _output.WriteLine($"✅ Final state: 1 block ({finalSlots.First()}) with {finalTxs} transactions");
        _output.WriteLine($"✅ Interface compliance: Used only ICardanoChainProvider methods");
        _output.WriteLine($"✅ Consumer control: Stopped chain sync after 5 blocks");
    }
}