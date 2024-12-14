

using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Cardano.Core.Extensions;
using Chrysalis.Cardano.Core.Types.Block;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

[ReducerDepends(typeof(BlockTestReducer))]
public class BlockDependencyTestReducer(
    IDbContextFactory<TestDbContext> dbContextFactory
) : IReducer<BlockTest>
{
    public Task RollBackwardAsync(ulong slot)
    {
        return Task.CompletedTask;
    }

    public Task RollForwardAsync(Block block)
    {
        // string blockHash = block.Hash();
        // ulong blockNumber = block.Number();
        // ulong slot = block.Slot();

        //using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        // dbContext.BlockTests.Add(new BlockTest(blockHash + "test", blockNumber, slot));

        // await dbContext.SaveChangesAsync();
        return Task.CompletedTask;
    }
}