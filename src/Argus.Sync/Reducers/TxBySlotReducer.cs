using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Core;
using Chrysalis.Cbor;
using Chrysalis.Utils;
using Microsoft.EntityFrameworkCore;
using Block = Chrysalis.Cardano.Core.Block;


namespace Argus.Sync.Reducers;

public class TxBySlotReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<TxBySlot> where T : CardanoDbContext, ITxBySlotDbContext
{

    public async Task RollForwardAsync(Block block)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();

        ulong slot = block.Slot();

        string hash = block.Hash();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        for (uint x = 0; x < transactions.Count(); x++)
        {
            dbContext.TxBySlot.Add(new(
                hash,
                slot,
                x,
                CborSerializer.Serialize(transactions.ElementAt((int)x))
            ));
        }

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task RollBackwardAsync(ulong slot)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();
        dbContext.TxBySlot.RemoveRange(
            dbContext
                .TxBySlot
                .AsNoTracking()
                .Where(b => b.Slot >= slot)
        );
        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task<ulong> QueryTip()
    {
        using T dbContext = await dbContextFactory.CreateDbContextAsync();
        ulong maxSlot = await dbContext.TxBySlot.MaxAsync(x => (ulong?)x.Slot) ?? 0;
        return maxSlot;
    }
}