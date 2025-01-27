using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Core.Extensions;
using Chrysalis.Cbor.Converters;
using Microsoft.EntityFrameworkCore;
using Block = Chrysalis.Cardano.Core.Types.Block.Block;

namespace Argus.Sync.Reducers;

public class BlockBySlotReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<BlockBySlot> where T : CardanoDbContext, IBlockBySlotDbContext
{
    public async Task RollForwardAsync(Block block)
    {
        await using T _dbContext = await dbContextFactory.CreateDbContextAsync();
        ulong slot = block.Slot() ?? 0UL;

        string hash = block.Hash();

        byte[] serializedBlock = CborSerializer.Serialize(block);

        _dbContext.BlockBySlot.Add(new(slot, hash, serializedBlock));

        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
    }

    public async Task RollBackwardAsync(ulong slot)
    {
        await using T _dbContext = dbContextFactory.CreateDbContext();

        _dbContext.BlockBySlot.RemoveRange(
            _dbContext.BlockBySlot.AsNoTracking().Where(b => b.Slot >= slot)
        );

        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
    }

    public async Task<ulong?> QueryTip()
    {
        using T dbContext = await dbContextFactory.CreateDbContextAsync();
        ulong maxSlot = await dbContext.BlockBySlot.MaxAsync(x => (ulong?)x.Slot) ?? 0;
        return maxSlot;
    }
}