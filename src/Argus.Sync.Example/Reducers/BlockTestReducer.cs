

using Argus.Sync.Example.Models;
using Argus.Sync.Extensions;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
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
        string blockHash = block.Header().Hash();
        ulong blockNumber = block.Header().HeaderBody().BlockNumber();
        ulong slot = block.Header().HeaderBody().Slot();

        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        dbContext.BlockTests.Add(new BlockTest(blockHash, blockNumber, slot, DateTime.UtcNow));

        await dbContext.SaveChangesAsync();
    }
}