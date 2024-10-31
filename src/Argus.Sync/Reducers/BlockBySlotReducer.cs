using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;
using Block = Chrysalis.Cardano.Models.Core.BlockEntity;

namespace Argus.Sync.Reducers;

public class BlockBySlotReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<BlockBySlot> where T : CardanoDbContext, IBlockBySlotDbContext
{
    public async Task RollForwardAsync(Block block)
    {
        await using T _dbContext = await dbContextFactory.CreateDbContextAsync();
        ulong slot = block.Slot();

        string hash = block.Hash();

        byte[] serializedBlock = CborSerializer.Serialize(block);

        _dbContext.BlockBySlot.Add(new BlockBySlot(slot, hash, serializedBlock));

        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
    }

    public async Task RollBackwardAsync(ulong slot)
    {
        T _dbContext = dbContextFactory.CreateDbContext();
        _dbContext = dbContextFactory.CreateDbContext();

        _dbContext.BlockBySlot.RemoveRange(
            _dbContext.BlockBySlot.AsNoTracking().Where(b => b.Slot >= slot)
        );
        
        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
    }
}