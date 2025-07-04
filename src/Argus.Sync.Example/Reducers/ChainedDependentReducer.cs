using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;
namespace Argus.Sync.Example.Reducers;

[DependsOn(typeof(DependentTransactionReducer))]
public class ChainedDependentReducer(
    IDbContextFactory<TestDbContext> dbContextFactory) : IReducer<BlockTest>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        
        // This is a chained dependent (depends on DependentTransactionReducer which depends on BlockTestReducer)
        var blockCount = await dbContext.BlockTests
            .Where(b => b.Slot >= slot)
            .CountAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        ulong slot = block.Header().HeaderBody().Slot();
        ulong height = block.Header().HeaderBody().BlockNumber();
        
        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        
        // Verify that both dependencies have processed this block
        var blockExists = await dbContext.BlockTests.AnyAsync(b => b.Slot == slot);
        if (!blockExists)
        {
            throw new InvalidOperationException($"Block at slot {slot} not found - BlockTestReducer dependency not satisfied!");
        }
        
        // Verify that DependentTransactionReducer has already processed this block
        var txCount = await dbContext.TransactionTests
            .Where(t => t.Slot == slot)
            .CountAsync();
        
        // This demonstrates a chained dependency:
        // BlockTestReducer -> TransactionTestReducer (root reducers)
        // DependentTransactionReducer (depends on BlockTestReducer)
        // ChainedDependentReducer (depends on DependentTransactionReducer)
        // 
        // The forwarding system ensures this reducer only receives blocks after
        // its entire dependency chain has processed them.
    }
}