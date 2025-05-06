using System.Linq.Expressions;
using Argus.Sync.Example.Models;
using Argus.Sync.Example.Utils;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Argus.Sync.Example.Reducers;

public class UtxosByAddressReducer(IDbContextFactory<TestDbContext> dbContextFactory) : IReducer<UtxoByAddress>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<UtxoByAddress> entriesToRemove = dbContext.UtxosByAddress.Where(t => t.Slot >= slot);

        dbContext.UtxosByAddress.RemoveRange(entriesToRemove);

        // This is to prevent stack overflow
        bool hasMoreEntries = true;
        int processedCount = 0;

        while (hasMoreEntries)
        {
            List<UtxoByAddress> entriesBatch = await dbContext.UtxosByAddress
                .Where(t => t.Slot >= slot)
                .OrderBy(t => t.Slot)
                .Skip(processedCount)
                .Take(200) // TODO - make this configurable
                .ToListAsync();

            if (!entriesBatch.Any())
            {
                hasMoreEntries = false;
                break;
            }

            processedCount += entriesBatch.Count;

            Expression<Func<OutputBySlot, bool>> predicate = PredicateBuilder.False<OutputBySlot>();
            entriesBatch.ForEach(entry =>
                predicate = predicate.Or(o => o.TxHash == entry.TxHash && o.TxIndex == entry.TxIndex)
            );

            List<OutputBySlot> utxoOutputs = await dbContext.OutputsBySlot
                .Where(predicate)
                .ToListAsync();

            List<UtxoByAddress> newEntries = [.. utxoOutputs
                .Select(e =>
                {
                    if (!TryDeserializeOutput(e.OutputRaw, out TransactionOutput output)) return null;

                    return new {
                        Output = output,
                        e.TxHash,
                        e.TxIndex
                    };
                })
                .Where(e => e != null && DatumUtils.TryGetBech32Address(e!.Output, out _))
                .Select(e => new UtxoByAddress(
                    e!.TxHash,
                    e.TxIndex,
                    slot,
                    new WalletAddress(e.Output.Address()).ToBech32(),
                    e.Output.Amount().Lovelace()
                ))];

            if (newEntries.Any())
            {
                dbContext.UtxosByAddress.AddRange(newEntries);
            }

            await dbContext.SaveChangesAsync();
        }
    }

    public async Task RollForwardAsync(Block block)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        ulong slot = block.Header().HeaderBody().Slot();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();
        transactions.ToList().ForEach(tx => ProcessOutput(tx, slot, dbContext));

        HashSet<(string txHash, ulong txIndex)> inputsTuple = [.. transactions
            .SelectMany(tx => tx.Inputs(),
            (_, input) => (txHash: Convert.ToHexStringLower(input.TransactionId()), txIndex: input.Index))];

        Expression<Func<UtxoByAddress, bool>> predicate = PredicateBuilder.False<UtxoByAddress>();
        inputsTuple.ToList().ForEach(input =>
        {
            predicate = predicate.Or(uba => uba.TxHash == input.txHash && uba.TxIndex == input.txIndex);
        });

        IQueryable<UtxoByAddress> utxosToRemove = dbContext.UtxosByAddress
            .Where(predicate);

        dbContext.UtxosByAddress.RemoveRange(utxosToRemove);

        await dbContext.SaveChangesAsync();
    }

    private static void ProcessOutput(
        TransactionBody tx,
        ulong slot,
        TestDbContext dbContext
    )
    {
        string txHash = tx.Hash();

        List<UtxoByAddress> newEntries = [.. tx.Outputs()
            .Select((output, index) => new { Output = output, Index = (ulong)index })
            .Where(e => DatumUtils.TryGetBech32Address(e.Output, out _))
            .Select(e =>
                new UtxoByAddress(
                    txHash,
                    e.Index,
                    slot,
                    new WalletAddress(e.Output.Address()).ToBech32(),
                    e.Output.Amount().Lovelace()
                ))];

        dbContext.UtxosByAddress.AddRange(newEntries);
    }

    private static bool TryDeserializeOutput(byte[] datum, out TransactionOutput output)
    {
        output = default!;

        try
        {
            TransactionOutput txOutput = CborSerializer.Deserialize<TransactionOutput>(datum);
            if (txOutput == null) return false;

            output = txOutput;
            return true;
        }
        catch
        {
            return false;
        }
    }
}