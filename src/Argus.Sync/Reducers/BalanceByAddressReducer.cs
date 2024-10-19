using Microsoft.EntityFrameworkCore;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Chrysalis.Cardano.Models.Core.Transaction;
using Argus.Sync.Utils;
using TransactionOutput = Chrysalis.Cardano.Models.Core.Transaction.TransactionOutput;
using TransactionOutputEntity = Argus.Sync.Data.Models.TransactionOutput;
using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Core;
namespace Argus.Sync.Reducers;

public class BalanceByAddressReducer<T>(IDbContextFactory<T> dbContextFactory) : IReducer<BalanceByAddress> where T : CardanoDbContext
{
    public async Task RollForwardAsync(Chrysalis.Cardano.Models.Core.Block.Block block)
    {
        await using CardanoDbContext _dbContext = await dbContextFactory.CreateDbContextAsync();

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
            .SelectMany(tx => tx.Inputs().Select(i => Convert.ToHexString(i.TransactionId.Value).ToLowerInvariant() + i.Index))
            .ToList();

        //get all the outputs that it matches with
        List<TransactionOutputEntity> matchedDbOutputs = await _dbContext.TransactionOutputs
            .Where(o => txInputs.Contains(o.Id + o.Index))
            .ToListAsync();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        foreach (TransactionBody tx in transactions)
        {
            ProcessInputsAsync(block.Slot(), tx, matchedDbOutputs, existingBAs, _dbContext);
            ProcessOutputsAsync(block.Slot(), tx, existingBAs, _dbContext);
        }

        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
    }

    private void ProcessInputsAsync(ulong slot, TransactionBody tx, List<TransactionOutputEntity> matchedDbOutputs, List<BalanceByAddress> existingBAs, CardanoDbContext _dbContext)
    {

        foreach (TransactionInput input in tx.Inputs())
        {
            var matchedOutput = matchedDbOutputs
                .FirstOrDefault(o => o.Id + o.Index == Convert.ToHexString(input.TransactionId.Value).ToLowerInvariant() + input.Index);

            if (matchedOutput != null)
            {
                string outputAddress = matchedOutput.Address;
                var updateBalance = existingBAs
                    .FirstOrDefault(ba => ba.Address == outputAddress);

                Value? balance = CborSerializer.Deserialize<Value>(matchedOutput.AmountCbor);

                if (updateBalance != null && balance != null)
                {
                    updateBalance.Balance -= balance.Lovelace();
                }
            }
        }

    }

    private void ProcessOutputsAsync(ulong slot, TransactionBody tx, List<BalanceByAddress> existingBAs, CardanoDbContext _dbContext)
    {

        foreach (TransactionOutput output in tx.Outputs())
        {

            string? Bech32Addr = output.Address().Value.ToBech32(); //Address.Raw.ToBech32(); //util in the making
            if (Bech32Addr is null || !Bech32Addr.StartsWith("addr")) continue;

            Console.WriteLine($"BECH2: {Bech32Addr}");

            if (existingBAs != null && existingBAs.Any())
            {
                foreach (var address in existingBAs)
                {
                    Console.WriteLine($"Address: {address.Address}, Balance: {address.Balance}");
                }
            }
            else
            {
                Console.WriteLine("No addresses found.");
            }

            var localAddress = _dbContext.BalanceByAddress.Local
                .FirstOrDefault(ba => ba.Address == Bech32Addr);

            //in Local
            if (localAddress != null)
            {
                localAddress.Balance += output.Amount().Lovelace();
            }
            else if (existingBAs?.FirstOrDefault(ba => ba.Address == Bech32Addr) != null)
            {
                //adds it in Local
                var dbAddress = existingBAs.First(ba => ba.Address == Bech32Addr);

                dbAddress.Balance += output.Amount().Lovelace();
            }
            else //neither in DB nor local
            {
                //also adds to local
                BalanceByAddress newBba = new()
                {
                    Address = Bech32Addr,
                    Balance = output.Amount().Lovelace()
                };

                _dbContext.BalanceByAddress.Add(newBba);
            }
        }

    }


    public async Task RollBackwardAsync(ulong slot)
    {
        await using CardanoDbContext _dbContext = await dbContextFactory.CreateDbContextAsync();

        //undo inputs and outputs, but what if naguna ang Rollback sa Input or Output niya gi remove na ang history?
        ulong rollbackSlot = slot;

        List<TransactionOutputEntity> outInRollbackEntries = await _dbContext.TransactionOutputs
                .AsNoTracking()
                .Where(tr => tr.Slot > rollbackSlot)
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

            Value? lovelaceBalance = CborSerializer.Deserialize<Value>(output.AmountCbor);

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