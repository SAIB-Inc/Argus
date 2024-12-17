

using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Cardano.Core.Extensions;
using Chrysalis.Cardano.Core.Types.Block;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class BlockTestReducer(
    IDbContextFactory<TestDbContext> dbContextFactory
) : IReducer<BlockTest>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        dbContext.BlockTests.RemoveRange(
            dbContext
                .BlockTests
                .AsNoTracking()
                .Where(b => b.Slot >= slot)
        );

        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        string blockHash = block.Hash();
        ulong blockNumber = block.Number() ?? 0UL;
        ulong slot = block.Slot() ?? 0UL;

        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        dbContext.BlockTests.Add(new BlockTest(blockHash, blockNumber, slot));

        await dbContext.SaveChangesAsync();
    }
}