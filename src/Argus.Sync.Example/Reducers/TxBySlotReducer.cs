using Argus.Sync.Example.Extensions;
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
// : IReducer<TxBySlot>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<TxBySlot> rollbackEntries = dbContext.TxsBySlot
            .Where(e => e.Slot >= slot);

        dbContext.RemoveRange(rollbackEntries);
        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        IEnumerable<TransactionBody> txBodies = block.TransactionBodies();
        if (!txBodies.Any()) return;

        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        ulong slot = block.Header().HeaderBody().Slot();

        List<TxBySlot>? txBySlotEntries = [.. block.TransactionBodies()
            .Where(tx => tx.Outputs().All(output => output.Datum() is null))
                .Select((tx, index) => new TxBySlot(
                    tx.Hash().ToLowerInvariant(),
                    (ulong)index,
                    slot,
                    tx.Raw!.Value.ToArray(),
                    [.. tx.Inputs().Select(input => input.Raw.HasValue ? input.Raw.Value.ToArray() : [])],
                    [.. tx.Outputs().Select(output => output.Raw.HasValue ? output.Raw.Value.ToArray() : [])],
                    tx.Fee()
                ))];

        if (!txBySlotEntries.Any()) return;

        dbContext.TxsBySlot.AddRange(txBySlotEntries);
        await dbContext.SaveChangesAsync();
    }
}