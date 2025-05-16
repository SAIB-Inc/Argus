using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class OutputBySlotReducer(IDbContextFactory<TestDbContext> dbContextFactory) : IReducer<OutputBySlot>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();
        await dbContext.OutputsBySlot
            .Where(t => t.Slot >= slot)
            .ExecuteDeleteAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        ulong slot = block.Header().HeaderBody().Slot();
        string blockHash = block.Header().Hash();
        ulong blockNumber = block.Header().HeaderBody().BlockNumber();

        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        IEnumerable<OutputBySlot> newEntries = transactions.SelectMany(tx => tx.Outputs().Select((output, idx) =>
            new OutputBySlot(
                tx.Hash(),
                (ulong)idx,
                slot,
                output.Raw.HasValue ? output.Raw.Value.ToArray() : CborSerializer.Serialize(output)
            )));
        
        dbContext.OutputsBySlot.AddRange(newEntries);
        await dbContext.SaveChangesAsync();
    }
}