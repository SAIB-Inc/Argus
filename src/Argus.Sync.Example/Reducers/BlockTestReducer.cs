using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
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

        _ = await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(IBlock block)
    {
        string blockHash = block.Header().Hash();
        ulong blockNumber = block.Header().HeaderBody().BlockNumber();
        ulong slot = block.Header().HeaderBody().Slot();

        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        _ = dbContext.BlockTests.Add(new BlockTest(blockHash, blockNumber, slot, DateTime.UtcNow));

        _ = await dbContext.SaveChangesAsync();
    }
}
