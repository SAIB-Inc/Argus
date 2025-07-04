using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;
namespace Argus.Sync.Example.Reducers;

[ReducerDepends(typeof(BlockTestReducer))]
public class DependentTransactionReducer(
    IDbContextFactory<TestDbContext> dbContextFactory) : IReducer<TransactionTest>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        
        // For demonstration, we'll just count what would be rolled back
        var txCount = await dbContext.TransactionTests
            .Where(t => t.Slot >= slot)
            .CountAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        ulong slot = block.Header().HeaderBody().Slot();
        
        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        
        // This reducer depends on BlockTestReducer, so blocks should already exist
        var blockExists = await dbContext.BlockTests.AnyAsync(b => b.Slot == slot);
        if (!blockExists)
        {
            throw new InvalidOperationException($"Block at slot {slot} not found - dependency not satisfied!");
        }
        
        // Count existing transactions at this slot
        var txCount = await dbContext.TransactionTests
            .Where(t => t.Slot == slot)
            .CountAsync();
        
        // Since this is a dependent reducer, we're just verifying that our dependency (BlockTestReducer) 
        // has already processed this block. In a real scenario, this reducer might process additional
        // transaction details or create derived data.
    }
}