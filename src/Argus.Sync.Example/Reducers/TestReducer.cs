using Argus.Sync.Data.Models;
using Argus.Sync.Example.Data;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Reducers;
using Argus.Sync.Workers;
using CardanoSharp.Wallet.Common;
using Chrysalis.Cardano.Models.Core.Block;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class TestReducer<T>(IDbContextFactory<T> dbContextFactory) : IReducer<TestModel> where T : CardanoTestDbContext
{
    private T _dbContext = default!;

    public async Task RollForwardAsync(Chrysalis.Cardano.Models.Core.Block.Block block)
    {
        ulong slot = block.Slot();
        ulong blockNum = block.BlockNumber();
        _dbContext = dbContextFactory.CreateDbContext();

        _dbContext.TestModels.Add(new TestModel
        {
            Slot = slot,
            BlockNumber = blockNum
        });

        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();
    }

    public async Task RollBackwardAsync(ulong slot)
    {
        _dbContext = dbContextFactory.CreateDbContext();
        _dbContext.TestModels.RemoveRange(_dbContext.TestModels.AsNoTracking().Where(b => b.Slot > slot));
        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();
    }
}