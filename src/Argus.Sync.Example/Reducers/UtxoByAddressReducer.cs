using System.Linq.Expressions;
using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Wallet.Models.Addresses;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

public class UtxoByAddressReducer(IDbContextFactory<TestDbContext> dbContextFactory
) : IReducer<UtxoByAddress>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<UtxoByAddress> rollbackTokenEntries = dbContext.UtxosByAddress
            .AsNoTracking()
            .Where(b => (b.Slot >= slot && b.Status == UtxoStatus.Unspent) ||
                (b.SpentSlot >= slot && b.Status == UtxoStatus.Spent));

        dbContext.UtxosByAddress.RemoveRange(rollbackTokenEntries);
        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        IEnumerable<TransactionBody> txBodies = block.TransactionBodies();
        if (!txBodies.Any()) return;

        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        ulong slot = block.Header().HeaderBody().Slot();
        ulong blockNumber = block.Header().HeaderBody().BlockNumber();
        IEnumerable<UtxoByAddress> outputEntities = ProcessOutputs(txBodies, slot, blockNumber);
        if (!outputEntities.Any()) return;
        dbContext.UtxosByAddress.AddRange(outputEntities);

        HashSet<(string txHash, ulong index)> inputOutRefs = [.. txBodies.SelectMany(tx =>
                tx.Inputs().Select(input =>
                    (txHash: Convert.ToHexStringLower(input.TransactionId()), index: input.Index)
                )
            )];
        Expression<Func<UtxoByAddress, bool>> predicate = PredicateBuilder.False<UtxoByAddress>();
        inputOutRefs.ToList().ForEach(input =>
            predicate = predicate.Or(o => o.TxHash == input.txHash && o.TxIndex == input.index && o.Status == UtxoStatus.Unspent));

        List<UtxoByAddress> dbEntries = await dbContext.UtxosByAddress
            .Where(predicate)
            .ToListAsync();

        List<UtxoByAddress> localEntries = [.. dbContext.UtxosByAddress.Local.Where(e => inputOutRefs.Contains((e.TxHash, e.TxIndex)))];
        HashSet<UtxoByAddress> allEntries = [.. dbEntries
            .Concat(localEntries)
            .GroupBy(e => (e.TxHash, e.TxIndex))
            .Select(g => g.First())];

        ProcessInputs(allEntries, outputEntities, slot, dbContext);
        await dbContext.SaveChangesAsync();
    }

    private static IEnumerable<UtxoByAddress> ProcessOutputs(IEnumerable<TransactionBody> transactionBodies, ulong slot, ulong blockNumber)
        => transactionBodies
            .SelectMany(txBody => txBody.Outputs()
                .Select((output, outputIndex) =>
                {
                    try
                    {
                        string bech32Addr = new Address(output.Address()).ToBech32();
                        if (!bech32Addr.StartsWith("addr")) return null;

                        return new UtxoByAddress(
                            txBody.Hash().ToLowerInvariant(),
                            (ulong)outputIndex,
                            slot,
                            bech32Addr,
                            blockNumber,
                            UtxoStatus.Unspent,
                            null,
                            output.Raw.HasValue ? output.Raw.Value.ToArray() : []
                        );
                    }
                    catch
                    {
                        return null;
                    }
                }))
            .Where(utxo => utxo is not null)!;

    private static void ProcessInputs(
        HashSet<UtxoByAddress> existingEntries,
        IEnumerable<UtxoByAddress> outputEntities, 
        ulong block, TestDbContext dbContext)
    {
        existingEntries.ToList().ForEach(entry => 
        {
            UtxoByAddress? localEntry = outputEntities
                .FirstOrDefault(e => e.TxHash == entry.TxHash && e.TxIndex == entry.TxIndex && e.Status == UtxoStatus.Unspent);

            localEntry ??= dbContext.UtxosByAddress.Local
                .FirstOrDefault(e => e.TxHash == entry.TxHash && e.TxIndex == entry.TxIndex && e.Status == UtxoStatus.Unspent);

            UtxoByAddress newEntry = entry with
            {
                SpentSlot = block, 
                Status = UtxoStatus.Spent
            };

            if (localEntry is not null)
                dbContext.UtxosByAddress.Add(newEntry);
        });
    }
}