using Microsoft.EntityFrameworkCore;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Chrysalis.Cardano.Models.Core.Transaction;
using Argus.Sync.Utils;
using TransactionOutput = Chrysalis.Cardano.Models.Core.Transaction.TransactionOutput;
using TransactionOutputEntity = Argus.Sync.Data.Models.OutputBySlot;
using Chrysalis.Cardano.Models.Core;
namespace Argus.Sync.Reducers;

public class BalanceByAddressReducer<T>(IDbContextFactory<T> dbContextFactory)
    : IReducer<BalanceByAddress> where T : BalanceByAddressDbContext, IBalanceByAddressDbContext
{
    public async Task RollForwardAsync(Chrysalis.Cardano.Models.Core.Block.Block block)
    {
        await using T _dbContext = await dbContextFactory.CreateDbContextAsync();

        //get all the output's addresses within the block
        var blockAddresses = block.TransactionBodies()
            .SelectMany(tx => tx.Outputs().Select(o => o.Address().Value.ToBech32()))
            .Distinct()
            .ToList();

        //get all the addresses in the DB that match blockAddresses
        var existingBAs = await _dbContext.BalanceByAddress
            .Where(ba => blockAddresses.Contains(ba.Address))
            .ToListAsync();

        //get all the inputs in the block
        var txInputs = block.TransactionBodies()
            .SelectMany(tx => tx.Inputs().Select(i => Convert.ToHexString(i.TransactionId.Value).ToLowerInvariant() + i.Index.Value))
            .ToList();

        //get all the outputs that it matches with
        List<TransactionOutputEntity> matchedDbOutputs = await _dbContext.OutputBySlot
            .Where(o => txInputs.Contains(o.Id + o.Index))
            .ToListAsync();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        foreach (TransactionBody tx in transactions)
        {
            ProcessInputs(block.Slot(), tx, matchedDbOutputs, existingBAs);
            ProcessOutputs(block.Slot(), tx, existingBAs, _dbContext);
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

            //in Local
            if (localAddress != null)
            {
                localAddress.Balance += output.Amount().Lovelace();
            }
            else if (existingBAs?.FirstOrDefault(ba => ba.Address == addr) != null)
            {
                //adds it in Local
                var dbAddress = existingBAs.First(ba => ba.Address == addr);

                dbAddress.Balance += output.Amount().Lovelace();
            }
            else //neither in DB nor local
            {
                //also adds to local
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

        //undo inputs and outputs, but what if naguna ang Rollback sa Input or Output niya gi remove na ang history?
        ulong rollbackSlot = slot;

        List<TransactionOutputEntity> outInRollbackEntries = await _dbContext.OutputBySlot
                .AsNoTracking()
                .Where(tr => tr.Slot >= rollbackSlot)
                .ToListAsync();

        var outInAddresses = outInRollbackEntries
            .Select(o => o.Address)
            .Distinct()
            .ToList();

        List<BalanceByAddress> balanceAddressEntries = await _dbContext.BalanceByAddress
            .Where(ba => outInAddresses.Contains(ba.Address))
            .ToListAsync();

        foreach (TransactionOutputEntity output in outInRollbackEntries)
        {
            //find the address
            var match = balanceAddressEntries
                .FirstOrDefault(ba => ba.Address == output.Address);

            Value lovelaceBalance = output.Amount;

            if (match != null && lovelaceBalance != null)
            {
                if (output.SpentSlot != null)
                {
                    //is an input
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