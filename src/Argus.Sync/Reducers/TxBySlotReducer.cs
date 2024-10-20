using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;


namespace Argus.Sync.Reducers;

public class TxBySlotReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<TxBySlot> where T : CardanoDbContext, ITxBySlotDbContext
{

    public async Task RollForwardAsync(Chrysalis.Cardano.Models.Core.Block.Block block)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();

        ulong slot = block.Slot();

        byte[] header = CborSerializer.Serialize(block!.Header);
        byte[] byteHash = ArgusUtils.ToBlake2b(header);
        string hash = Convert.ToHexString(byteHash).ToLowerInvariant();

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

}