using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class TransactionTestReducer : IReducer
{
    public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        TestDbContext dbContext = uow.GetStorage<TestDbContext>();
        dbContext.TransactionTests.RemoveRange(
            dbContext.TransactionTests
                .AsNoTracking()
                .Where(t => t.Slot >= slot));
        return Task.CompletedTask;
    }

    public Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(uow);
        TestDbContext dbContext = uow.GetStorage<TestDbContext>();

        ulong slot = block.Header().HeaderBody().Slot();
        string blockHash = block.Header().Hash();
        ulong blockHeight = block.Header().HeaderBody().BlockNumber();

        ulong index = 0;
        foreach (ITransactionBody tx in block.TransactionBodies())
        {
            string txHash = tx.Hash();
            _ = dbContext.TransactionTests.Add(new TransactionTest(txHash, index++, slot, blockHash, blockHeight, tx.Raw.ToArray(), DateTimeOffset.UtcNow));
        }
        return Task.CompletedTask;
    }
}
