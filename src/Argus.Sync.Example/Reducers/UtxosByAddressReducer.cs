using Argus.Sync.Example.Models;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Reducers;
using Chrysalis.Extensions;
using Microsoft.EntityFrameworkCore;
using Block = Chrysalis.Cardano.Core.Block;

namespace Argus.Sync.Example.Reducers;

public class UtxosByAddressReducer(IDbContextFactory<TestDbContext> dbContextFactory)
    : IReducer<UtxosByAddress>
{
    public async Task RollForwardAsync(Block block)
    {
        // API surface is still work in progress
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        // Process Outputs
        IEnumerable<UtxosByAddress> utxos = block.TransactionBodies()
            .SelectMany(tx => tx.Outputs()
                .Select((output, index) => new UtxosByAddress(
                    tx.Id(),
                    index,
                    block.Slot(),
                    output.Address()!.Value!.ToBech32()!,
                    output.Amount()!.GetCoin()
                ))
            )
            .Where(utxo => utxo.Address != null);

        dbContext.UtxosByAddress.AddRange(utxos);

        // Process Inputs
        IEnumerable<string> inputs = block.TransactionBodies()
            .SelectMany(tx => tx.Inputs()
                .Select((input, index) => $"{input.TransactionId()}{(int)input.Index()}")
            );

        IQueryable<UtxosByAddress> utxosToRemove = dbContext.UtxosByAddress
            .Where(utxo => inputs.Contains(utxo.TxHash + utxo.TxIndex));

        dbContext.UtxosByAddress.RemoveRange(utxosToRemove);

        await dbContext.SaveChangesAsync();
    }

    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<UtxosByAddress> utxosToRemove = dbContext.UtxosByAddress
            .Where(utxo => utxo.Slot >= slot);

        dbContext.UtxosByAddress.RemoveRange(utxosToRemove);

        await dbContext.SaveChangesAsync();
    }

    public async Task<ulong?> QueryTip()
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.UtxosByAddress.Select(e => e.Slot).FirstOrDefaultAsync();
    }
}