using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class BlockTestReducer : IReducer
{
    public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        TestDbContext dbContext = uow.GetStorage<TestDbContext>();
        dbContext.BlockTests.RemoveRange(
            dbContext.BlockTests
                .AsNoTracking()
                .Where(b => b.Slot >= slot));
        return Task.CompletedTask;
    }

    public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(uow);
        TestDbContext dbContext = uow.GetStorage<TestDbContext>();

        string blockHash = block.Header().Hash();
        ulong blockNumber = block.Header().HeaderBody().BlockNumber();
        ulong slot = block.Header().HeaderBody().Slot();

        _ = dbContext.BlockTests.Add(new BlockTest(blockHash, blockNumber, slot, DateTime.UtcNow));
        return Task.CompletedTask;
    }
}
