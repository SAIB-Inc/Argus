

using Argus.Sync.Example.Models;
using Argus.Sync.Extensions;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class TransactionTestReducer(IDbContextFactory<TestDbContext> dbContextFactory) : IReducer<TransactionTest>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        dbContext.TransactionTests.RemoveRange(
            dbContext.TransactionTests
                .AsNoTracking()
                .Where(t => t.Slot >= slot
            )
        );

        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        ulong slot = block.Header().HeaderBody().Slot();
        string blockHash = block.Header().Hash();
        ulong blockNumber = block.Header().HeaderBody().BlockNumber();

        using TestDbContext dbContext = dbContextFactory.CreateDbContext();

        ulong index = 0;
        foreach (var tx in block.TransactionBodies())
        {
            string txHash = tx.Hash();
            dbContext.TransactionTests.Add(new TransactionTest(txHash, index++, slot, blockHash, blockNumber, tx.Raw?.ToArray() ?? [], DateTimeOffset.UtcNow));
        }

        await dbContext.SaveChangesAsync();
    }
}