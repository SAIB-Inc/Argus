

using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Cardano.Core.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class BlockTestReducer(
    IDbContextFactory<TestDbContext> dbContextFactory
) : IReducer<BlockTest>
{
    public async Task<ulong?> QueryTip()
    {
        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        ulong maxSlot = await dbContext.BlockTests.MaxAsync(x => (ulong?)x.Slot) ?? 0;
        return maxSlot;
    }

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

    public async Task RollForwardAsync(Chrysalis.Cardano.Core.Types.Block.Block block)
    {
        string blockHash = block.Hash();
        ulong blockNumber = block.Number();
        ulong slot = block.Slot();

        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        dbContext.BlockTests.Add(new BlockTest(blockHash, blockNumber, slot));

        await dbContext.SaveChangesAsync();
    }
}