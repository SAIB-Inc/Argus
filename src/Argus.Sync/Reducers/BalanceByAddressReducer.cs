using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Argus.Sync.Utils;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions;
using Block = Chrysalis.Cardano.Core.Block;
using TransactionOutputEntity = Argus.Sync.Data.Models.OutputBySlot;
using Chrysalis.Cardano.Core;
using Chrysalis.Utils;
using Argus.Sync.Extensions.Chrysalis;

namespace Argus.Sync.Reducers;
//[ReducerDepends([typeof(OutputBySlotReducer<>)])]
public class BalanceByAddressReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<BalanceByAddress> where T : BalanceByAddressDbContext, IBalanceByAddressDbContext
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();

        List<TransactionOutputEntity> rollbackEntries = await dbContext.OutputBySlot
            .AsNoTracking()
            .Where(tr => tr.Slot >= slot)
            .ToListAsync();

        List<string> addresses = rollbackEntries
            .Select(o => o.Address)
            .Distinct()
            .ToList();

        Expression<Func<BalanceByAddress, bool>> predicate = PredicateBuilder.False<BalanceByAddress>();

        addresses.ForEach(addr =>
        {
            predicate = predicate.Or(ba => ba.Address == addr);
        });

        List<BalanceByAddress> balanceAddressEntries = await dbContext.BalanceByAddress
            .Where(predicate)
            .ToListAsync();

        foreach (TransactionOutputEntity output in rollbackEntries)
        {
            BalanceByAddress? address = balanceAddressEntries
                .FirstOrDefault(ba => ba.Address == output.Address);

            if(address is not null)
            {
                if (output.SpentSlot != null)
                {
                    address.Balance += output.Amount.Lovelace();
                }
                else
                {
                    address.Balance -= output.Amount.Lovelace();
                }
            }
        }

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();

        List<string> blockAddresses = block.TransactionBodies()
            .SelectMany(
                tx => tx.Outputs(),
                (_, output) => output.AddressValue().ToBech32()
            )
            .Where(addr => !string.IsNullOrEmpty(addr))
            .Select(addr => addr!)
            .Distinct()
            .ToList();

        Expression<Func<BalanceByAddress, bool>> predicate = PredicateBuilder.False<BalanceByAddress>();

        blockAddresses.ForEach(addr =>
        {
            predicate = predicate.Or(ba => ba.Address == addr);
        });

        List<BalanceByAddress> existingAddresses = await dbContext.BalanceByAddress
            .Where(predicate)
            .ToListAsync();

        foreach (TransactionBody tx in block.TransactionBodies())
        {
            ProcessOutputs(tx, existingAddresses, dbContext);
        }

        List<(string TxHash, ulong TxIndex)> inputsTuple = block.TransactionBodies()
            .SelectMany(
                tx => tx.Inputs(),
                (_, input) => (input.TransacationId(), input.Index())
            )
            .ToList();

        Expression<Func<TransactionOutputEntity, bool>> inputsPredicate = PredicateBuilder.False<TransactionOutputEntity>();

        inputsTuple.ForEach(input =>
        {
            inputsPredicate = inputsPredicate.Or(tr => tr.Id == input.TxHash && tr.Index == input.TxIndex);
        });

        List<TransactionOutputEntity> existingOutputEntries = await dbContext.OutputBySlot
            .AsNoTracking()
            .Where(inputsPredicate)
            .ToListAsync();

        foreach (TransactionBody tx in block.TransactionBodies())
        {
            ProcessInputs(tx, existingOutputEntries, existingAddresses);
        }

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    private void ProcessInputs(TransactionBody tx, List<TransactionOutputEntity> existingOutputEntries, List<BalanceByAddress> existingAddresses)
    {
        foreach (TransactionInput input in tx.Inputs())
        {
            TransactionOutputEntity? utxo = existingOutputEntries
                .FirstOrDefault(o => o.Id == input.TransacationId() && o.Index == input.Index());
            if(utxo == null) continue;

            BalanceByAddress? address = existingAddresses
                .FirstOrDefault(ba => ba.Address == utxo.Address);
            if(address == null) continue;

            address.Balance -= utxo.Amount.Lovelace();
        }
    }

    private void ProcessOutputs(TransactionBody tx, List<BalanceByAddress> existingAddresses, T dbContext)
    {
        foreach (TransactionOutput output in tx.Outputs())
        {
            string? addr = output.AddressValue().ToBech32();

            if (addr is not null && addr.StartsWith("addr"))
            {
                BalanceByAddress? address = existingAddresses.FirstOrDefault(ba => ba.Address == addr);

                if (address != null)
                {
                    address.Balance += output.Amount()!.Lovelace();
                }
                else
                {
                    BalanceByAddress newAddress = new(addr, output.Amount()!.Lovelace());

                    dbContext.BalanceByAddress.Add(newAddress);
                    existingAddresses.Add(newAddress);
                }
            }
        }
    }
}
