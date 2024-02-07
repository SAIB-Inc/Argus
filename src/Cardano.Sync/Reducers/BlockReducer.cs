using Cardano.Sync.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PallasDotnet.Models;
using BlockEntity = Cardano.Sync.Data.Models.Block;
namespace Cardano.Sync.Reducers;

public class BlockReducer<T>(IDbContextFactory<T> dbContextFactory) : IBlockReducer where T : CardanoDbContext
{
    private T _dbContext = default!;

    public async Task RollForwardAsync(NextResponse response)
    {
        _dbContext = dbContextFactory.CreateDbContext();
        _dbContext.Blocks.Add(new BlockEntity(
            response.Block.Hash.ToHex(),
            response.Block.Number,
            response.Block.Slot
        ));

        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();
    }
    
    public async Task RollBackwardAsync(NextResponse response)
    {
        _dbContext = dbContextFactory.CreateDbContext();
        _dbContext.Blocks.RemoveRange(_dbContext.Blocks.AsNoTracking().Where(b => b.Slot > response.Block.Slot));
        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();
    }

}