
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Reducers;

public class BlockBySlotReducer<T>(IDbContextFactory<T> dbContextFactory) : IReducer<BlockBySlot> where T : CardanoDbContext
{
    private T _dbContext = default!;

    public async Task RollForwardAsync(Chrysalis.Cardano.Models.Core.Block.Block block)
    {
               
        _dbContext = dbContextFactory.CreateDbContext();
      
        ulong slot = block.Slot();

        byte[] header = CborSerializer.Serialize(block!.Header);
        byte[] byteHash = ArgusUtils.ToBlake2b(header);
        string hash = Convert.ToHexString(byteHash).ToLowerInvariant(); 

        byte[] serializedBlock = CborSerializer.Serialize(block);

        _dbContext.BlockBySlot.Add(new BlockBySlot
        {
            Slot = slot,
            Hash = hash,
            Block = serializedBlock
        });

        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();

    }
    
    public async Task RollBackwardAsync(ulong slot)
    {

        _dbContext = dbContextFactory.CreateDbContext();
        _dbContext.BlockBySlot.RemoveRange(_dbContext.BlockBySlot.AsNoTracking().Where(b => b.Slot > slot));
        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();

    }

}