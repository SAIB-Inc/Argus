using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Wallet.Models.Addresses;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class UtxoByAddressReducer(IDbContextFactory<TestDbContext> dbContextFactory) 
// : IReducer<UtxoByAddress>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<UtxoByAddress> rollbackEntries = dbContext.UtxosByAddress
            .Where(e => e.Slot >= slot);

        dbContext.RemoveRange(rollbackEntries);
        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        if (!block.TransactionBodies().Any()) return;

        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        ulong slot = block.Header().HeaderBody().Slot();
        IEnumerable<UtxoByAddress> outputEntities = block.TransactionBodies()
            .SelectMany(txBody => txBody.Outputs().Select((output, outputIndex) =>
                new UtxoByAddress(
                    txBody.Hash().ToLowerInvariant(),
                    outputIndex,
                    slot,
                    new Address(output.Address()).ToBech32(),
                    block.Header().HeaderBody().BlockNumber(),
                    output.Raw.HasValue ? output.Raw.Value.ToArray() : []
                )))
            .Where(entity => entity != null);

        dbContext.UtxosByAddress.AddRange(outputEntities);
        await dbContext.SaveChangesAsync();
    }
}