using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using TransactionOutput = Chrysalis.Cardano.Models.Core.Block.Transaction.TransactionOutput;
using TransactionOutputEntity = Argus.Sync.Data.Models.OutputBySlot;
using Chrysalis.Cardano.Models.Core.Block.Transaction;
using Block = Chrysalis.Cardano.Models.Core.BlockEntity;
using Chrysalis.Cardano.Models.Core.Block.Transaction.Output;
using Argus.Sync.Extensions;

namespace Argus.Sync.Reducers;

public class BalanceByAddressReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<BalanceByAddress> where T : BalanceByAddressDbContext, IBalanceByAddressDbContext
{
    public async Task RollForwardAsync(Block block)
    {
        await using T _dbContext = await dbContextFactory.CreateDbContextAsync();

        Expression<Func<BalanceByAddress, bool>> predicate = PredicateBuilder.False<BalanceByAddress>();

        var blockAddresses = block.TransactionBodies()
            .SelectMany(tx => tx.Outputs().Select(o => o.Address().Value.ToBech32()))
            .Distinct()
            .ToList();

        predicate = predicate.Or(ba => blockAddresses.Contains(ba.Address));

        var existingBAs = await _dbContext.BalanceByAddress
            .Where(predicate)
            .ToListAsync();

        Expression<Func<OutputBySlot, bool>> predicateObyS = PredicateBuilder.False<OutputBySlot>();

        var txInputs = block.TransactionBodies()
            .SelectMany(tx => tx.Inputs().Select(i => Convert.ToHexString(i.TransactionId.Value).ToLowerInvariant() + i.Index.Value))
            .ToList();

        predicateObyS = predicateObyS.Or(obs => txInputs.Contains(obs.Id + obs.Index));
        List<TransactionOutputEntity> matchedDbOutputs = await _dbContext.OutputBySlot
            .Where(predicateObyS)
            .ToListAsync();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        foreach (TransactionBody tx in transactions)
        {
            ProcessOutputs(block.Slot(), tx, existingBAs, _dbContext);
            ProcessInputs(block.Slot(), tx, matchedDbOutputs, existingBAs);
        }

        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
    }

    private void ProcessInputs(ulong slot, TransactionBody tx, List<TransactionOutputEntity> matchedDbOutputs, List<BalanceByAddress> existingBAs)
    {
        foreach (TransactionInput input in tx.Inputs())
        {
            var matchedOutput = matchedDbOutputs
                .FirstOrDefault(o => o.Id == input.TransacationId() && o.Index == input.Index());

            if (matchedOutput != null)
            {
                string outputAddress = matchedOutput.Address;
                var updateBalance = existingBAs
                    .FirstOrDefault(ba => ba.Address == outputAddress);

                Value? balance = matchedOutput.Amount;

                if (updateBalance != null && balance != null)
                {
                    updateBalance.Balance -= balance.Lovelace();
                }
            }
            else
            {
                continue;
            }
        }
    }

    private void ProcessOutputs(ulong slot, TransactionBody tx, List<BalanceByAddress> existingBAs, T dbContext)
    {
        foreach (TransactionOutput output in tx.Outputs())
        {
            string? addr = output.Address().Value.ToBech32();
            if (addr is null || !addr.StartsWith("addr")) continue;

            var localAddress = dbContext.BalanceByAddress.Local
                .FirstOrDefault(ba => ba.Address == addr);

            if (localAddress != null)
            {
                localAddress.Balance += output.Amount().Lovelace();
            }
            else if (existingBAs?.FirstOrDefault(ba => ba.Address == addr) != null)
            {
                var dbAddress = existingBAs.First(ba => ba.Address == addr);

                dbAddress.Balance += output.Amount().Lovelace();
            }
            else 
            {
                BalanceByAddress newBba = new(
                    addr,
                    output.Amount().Lovelace()
                );
                dbContext.BalanceByAddress.Add(newBba);
            }
        }

    }

    public async Task RollBackwardAsync(ulong slot)
    {
        await using T _dbContext = await dbContextFactory.CreateDbContextAsync();

        ulong rollbackSlot = slot;

        Expression<Func<OutputBySlot, bool>> predicate = PredicateBuilder.False<OutputBySlot>();

        predicate = predicate.Or(tr => tr.Slot >= rollbackSlot);
        List<TransactionOutputEntity> outInRollbackEntries = await _dbContext.OutputBySlot
                .AsNoTracking()
                .Where(tr => tr.Slot >= rollbackSlot)
                .ToListAsync();

        var outInAddresses = outInRollbackEntries
            .Select(o => o.Address)
            .Distinct()
            .ToList();

        Expression<Func<BalanceByAddress, bool>> balancePredicate = PredicateBuilder.False<BalanceByAddress>();
        balancePredicate = balancePredicate.Or(ba => outInAddresses.Contains(ba.Address));

        List<BalanceByAddress> balanceAddressEntries = await _dbContext.BalanceByAddress
            .Where(ba => outInAddresses.Contains(ba.Address))
            .ToListAsync();

        foreach (TransactionOutputEntity output in outInRollbackEntries)
        {
            var match = balanceAddressEntries
                .FirstOrDefault(ba => ba.Address == output.Address);

            Value lovelaceBalance = output.Amount;

            if (match != null && lovelaceBalance != null)
            {
                if (output.SpentSlot != null)
                {
                    match.Balance += lovelaceBalance.Lovelace();
                }
                else
                {
                    match.Balance -= lovelaceBalance.Lovelace();
                }
            }
        }

        await _dbContext.SaveChangesAsync();
    }
}