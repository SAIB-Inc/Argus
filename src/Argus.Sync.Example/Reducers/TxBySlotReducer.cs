using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class TxBySlotReducer(IDbContextFactory<TestDbContext> dbContextFactory)
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<TxBySlot> rollbackEntries = dbContext.TxBySlot
            .Where(e => e.Slot >= slot);

        dbContext.RemoveRange(rollbackEntries);
        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IEnumerable<TransactionBody> txBodies = block.TransactionBodies();
        ulong slot = block.Header().HeaderBody().Slot();

        IEnumerable<TxBySlot> txBySlotEntries = block
            .TransactionBodies()
                .Select((tx, index) => new TxBySlot(
                    tx.Hash(),
                    (ulong)index,
                    slot,
                    tx.Raw!.Value.ToArray()
                ));

        dbContext.TxBySlot.AddRange(txBySlotEntries);
        await dbContext.SaveChangesAsync();
    }
}