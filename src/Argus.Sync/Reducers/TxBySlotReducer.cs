using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;


namespace Argus.Sync.Reducers;

public class TxBySlotReducer<T>(IDbContextFactory<T> dbContextFactory) : IReducer<TxBySlot> where T : CardanoDbContext
{
    private T _dbContext = default!;

    public async Task RollForwardAsync(Chrysalis.Cardano.Models.Core.Block.Block block)
    {
        _dbContext = dbContextFactory.CreateDbContext();


        ulong slot = block.Slot();

        byte[] header = CborSerializer.Serialize(block!.Header);
        byte[] byteHash = ArgusUtils.ToBlake2b(header);
        string hash = Convert.ToHexString(byteHash).ToLowerInvariant(); 

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        foreach(TransactionBody tx in transactions)
        {
            byte[] serializedTx = CborSerializer.Serialize(tx);

            _dbContext.TxBySlot.Add( new TxBySlot
            {
                BlockSlot = slot,
                BlockHash = hash,
                Transaction = serializedTx
            });
        }
        
        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();

    }
    
    public async Task RollBackwardAsync(ulong slot)
    {

        _dbContext = dbContextFactory.CreateDbContext();
        _dbContext.TxBySlot.RemoveRange(_dbContext.TxBySlot.AsNoTracking().Where(b => b.BlockSlot > slot));
        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();

    }

}