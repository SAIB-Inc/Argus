using System.Linq.Expressions;
using Argus.Sync.Example.Extensions;
using Argus.Sync.Example.Models;
using Argus.Sync.Example.Models.Enums;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using Address = Chrysalis.Wallet.Models.Addresses.Address;

namespace Argus.Sync.Example.Reducers;

public class OwnerBySlotReducer(
    IDbContextFactory<TestDbContext> dbContextFactory
) : IReducer<OwnerBySlot>
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<OwnerBySlot> rollbackEntries =  dbContext.AssetOwnerBySlot
            .Where(x => x.Slot >= slot);
        
        dbContext.RemoveRange(rollbackEntries);
        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        if (!block.TransactionBodies().Any()) return;

        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        transactions.ToList().ForEach(transaction => ProcessOutputs(
            transaction,
            dbContext,
            block
        ));

        List<(TransactionBody Tx, string OutRef)> inputsTuple = [.. GetInputTuples(transactions)];

        Expression<Func<OwnerBySlot, bool>> historicalOwnerPredicate = PredicateBuilder.False<OwnerBySlot>();
        inputsTuple.ForEach(input =>
        {
            historicalOwnerPredicate = historicalOwnerPredicate.Or(hao => hao.OutRef == input.OutRef);
        });

        IEnumerable<string> inputOutRefs = inputsTuple.Select(inputTuple => inputTuple.OutRef);

        List<OwnerBySlot> existingHistoricalOwners = await dbContext.AssetOwnerBySlot
            .AsNoTracking()
            .Where(e => inputOutRefs.Contains(e.OutRef))
            .ToListAsync();

        List<OwnerBySlot> assetHistoricalOwners = [.. dbContext.AssetOwnerBySlot.Local.Where(e => inputOutRefs.Contains(e.OutRef))];

        List<OwnerBySlot> combinedHistoricalOwners = [.. existingHistoricalOwners.Union(assetHistoricalOwners)];

        await ProcessInputs(block, dbContext, inputsTuple, combinedHistoricalOwners);

        await dbContext.SaveChangesAsync();
    }

    private static Task ProcessOutputs(TransactionBody transaction, TestDbContext dbContext, Block block)
    {
        ulong slot = block.Header().HeaderBody().Slot();
        transaction.Outputs()
            .Select((output, index) => new { Output = output, Index = (ulong)index })
            .ToList().ForEach(e =>
            {
                string? address = new Address(e.Output.Address()).ToBech32();
                if (string.IsNullOrEmpty(address) || !address.StartsWith("addr")) return;

                MultiAssetOutput? multiAssetOutput = GetMultiAssetOutput(e.Output);
                if (multiAssetOutput is null) return;

                IEnumerable<(string PolicyId, string AssetName)>? subjectList = multiAssetOutput.SubjectTuples();
                if (subjectList is null) return;

                subjectList.ToList().ForEach(sl =>
                {
                    string subject = sl.PolicyId + sl.AssetName;
                    string outRef = transaction.Hash() + e.Index;

                    OwnerBySlot? existingAssetOwnerHistory = dbContext.AssetOwnerBySlot.Local
                        .FirstOrDefault(aoh => aoh.Address == address && aoh.Subject == subject && aoh.Slot == slot && aoh.OutRef == outRef);

                    if (existingAssetOwnerHistory is not null) return;

                    Dictionary<string, ulong> tokenBundle = multiAssetOutput?.TokenBundleByPolicyId(sl.PolicyId) ?? [];

                    if (!tokenBundle.TryGetValue(sl.AssetName, out ulong assetQty)) return;

                    OwnerBySlot assetOwnerHistory = new(
                        address,
                        subject,
                        sl.PolicyId,
                        outRef,
                        assetQty,
                        slot,
                        UtxoType.Output,
                        null
                    );

                    dbContext.AssetOwnerBySlot.Add(assetOwnerHistory);
                });
            });

        return Task.CompletedTask;
    }

    private static Task ProcessInputs(
        Block block,
        TestDbContext dbContext,
        List<(TransactionBody Tx, string OutRef)> inputsTuple,
        List<OwnerBySlot> combinedHistoricalOwners
    )
    {
        ulong slot = block.Header().HeaderBody().Slot();
        combinedHistoricalOwners.ForEach(hao =>
        {
            TransactionBody? tx = inputsTuple
                .Where(e => e.OutRef == hao.OutRef)
                .Select(e => e.Tx)
                .FirstOrDefault();

            if (tx is null) return;

            OwnerBySlot? existingAssetOwnerHistory = dbContext.AssetOwnerBySlot.Local
                .FirstOrDefault(aoh => aoh.Address == hao.Address && aoh.Subject == hao.Subject && aoh.Slot == slot && aoh.OutRef == hao.OutRef);

            if (existingAssetOwnerHistory is not null) return;

            OwnerBySlot? newAssetOwnerHistory = new(
                hao.Address,
                hao.Subject,
                hao.PolicyId,
                hao.OutRef,
                hao.Quantity,
                slot,
                UtxoType.Input,
                tx.Hash()
            );

            dbContext.AssetOwnerBySlot.Add(newAssetOwnerHistory);
        });

        return Task.CompletedTask;
    }

    protected IEnumerable<(TransactionBody Tx, string OutRef)> GetInputTuples(IEnumerable<TransactionBody> self)
    {
        return self.SelectMany(
            tx => tx.Inputs(),
            (tx, input) => (
                tx,
                Convert.ToHexString(input.TransactionId).ToLowerInvariant() + input.Index.ToString()
            )
        );
    }

    protected static MultiAssetOutput? GetMultiAssetOutput(TransactionOutput self) => new(self.Amount().MultiAsset());
}