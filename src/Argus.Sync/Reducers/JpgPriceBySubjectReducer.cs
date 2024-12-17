using Argus.Sync.Data;
using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Argus.Sync.Data.Models.Jpg;
using Block = Chrysalis.Cardano.Core.Types.Block.Block;
using JpgListing = Chrysalis.Cardano.Jpg.Types.Datums.Listing;
using Chrysalis.Cardano.Core.Extensions;
using System.Linq.Expressions;
using Argus.Sync.Extensions;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

namespace Argus.Sync.Reducers;

public class JpgPriceBySubjectReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<PriceByToken> where T : JpgPriceByTokenDbContext, IJpgPriceByTokenDbContext
{
    private readonly string _jpegV1validatorPkh = configuration.GetValue<string>("JPGStoreMarketplaceV1ValidatorScriptHash")!;

    public async Task RollBackwardAsync(ulong slot)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();

        List<PriceByToken> jpgListingEntriesToDelist = await dbContext.PriceByToken
            .Where(e => e.SpentSlot >= slot)
            .ToListAsync();

        jpgListingEntriesToDelist.ForEach(e =>
        {
            e.SpentSlot = null;
            e.Status = UtxoStatus.Unspent;
        });

        dbContext.PriceByToken.RemoveRange(
            dbContext.PriceByToken
                .Where(e => e.Slot >= slot)
        );

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        if (!block.TransactionBodies().Any()) return;

        await using T dbContext = await dbContextFactory.CreateDbContextAsync();

        ProcessOutputs(block, dbContext);

        List<(string TxHash, string TxIndex)> inputsTuple = block.TransactionBodies()
            .SelectMany(
                tx => tx.Inputs(),
                (_, input) => (
                    Convert.ToHexString(input.TransactionId.Value).ToLowerInvariant(),
                    input.Index.Value.ToString()
                )
            ).ToList();

        Expression<Func<PriceByToken, bool>> predicate = PredicateBuilder.False<PriceByToken>();

        inputsTuple.ForEach(inputTuple =>
        {
            predicate = predicate.Or(jpg => jpg.TxHash == inputTuple.TxHash && jpg.TxIndex.ToString() == inputTuple.TxIndex);
        });

        List<PriceByToken> jpgListingEntries = await dbContext.PriceByToken
            .Where(predicate)
            .ToListAsync();

        IEnumerable<string> inputOutRefs = inputsTuple.Select(inputTuple => inputTuple.TxHash + inputTuple.TxIndex);

        List<PriceByToken> jpgListingEntriesLocal = dbContext.PriceByToken.Local
            .Where(e => inputOutRefs.Contains(e.TxHash + e.TxIndex))
            .ToList();

        jpgListingEntries.AddRange(jpgListingEntriesLocal);

        ProcessInputs(block, jpgListingEntries, dbContext);

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    private void ProcessInputs(Block block, List<PriceByToken> jpgListingEntries, T dbContext)
    {
        jpgListingEntries.ForEach(e =>
        {
            e.SpentSlot = block.Slot();
            e.Status = UtxoStatus.Spent;
        });
    }

    private void ProcessOutputs(Block block, T dbContext)
    {

        List<PriceByToken> jpgPriceBySubjects = block.TransactionBodies()
            .SelectMany((tx, txIndexInBlock) =>
            {
                bool txHasJpgOutput = tx.Outputs().Any(e =>
                {
                    string pkh = Convert.ToHexString(e.Address()!.GetPublicKeyHash());
                    return pkh.Equals(_jpegV1validatorPkh, StringComparison.InvariantCultureIgnoreCase);
                });

                if (!txHasJpgOutput) return [];

                int? cborTxIndexInBlock = block.AuxiliaryDataSet()
                    .Keys
                    .FirstOrDefault(key => key == txIndexInBlock);

                Dictionary<string, byte[]> metadata =
                    cborTxIndexInBlock is not null
                        ? JpgUtils.MapMetadataToDatumDictionary(block.AuxiliaryDataSet()[cborTxIndexInBlock.Value])
                        : [];

                return tx.Outputs()
                    .Select((o, outputIdx) =>
                    {
                        string pkh = Convert.ToHexString(o.Address()!.GetPublicKeyHash());
                        bool isJpgOutput = pkh.Equals(
                            _jpegV1validatorPkh,
                            StringComparison.InvariantCultureIgnoreCase
                        );
                        if (!isJpgOutput) return null;

                        (DatumType DatumType, byte[]? RawData)? datumInfo = o.DatumInfo();

                        if (datumInfo is null) return null;

                        byte[]? datum = null;

                        if (
                            datumInfo.Value.DatumType is DatumType.Hash &&
                            metadata.TryGetValue(Convert.ToHexString(datumInfo.Value.RawData ?? []), out byte[]? rawDatum) &&
                            rawDatum != null
                        )
                        {
                            datum = rawDatum;
                        }

                        JpgListing? listing = CborSerializer.Deserialize<JpgListing>(datum ?? []);
                        if (listing is null) return null;

                        Value outputAmount = o.Amount()!;
                        MultiAssetOutput? multiAssetOutput = outputAmount!.MultiAssetOutput();
                        if (multiAssetOutput is null) return null;

                        ulong totalPayoutAmount = listing.Payouts.Value
                            .Aggregate(0UL, (acc, payout) => acc + payout.Amount.Value);


                        string subject = multiAssetOutput.Subjects().First().ToLowerInvariant();

                        double jpgFee = 0.02;
                        ulong jpgMinFee = 1000000;
                        ulong price = ((ulong)(totalPayoutAmount * jpgFee)) > jpgMinFee
                            ? (ulong)(totalPayoutAmount * (1 + jpgFee))
                            : (totalPayoutAmount + jpgMinFee);

                        return new PriceByToken(
                            block.Slot() ?? 0UL,
                            null,
                            tx.Id(),
                            (ulong)outputIdx,
                            price,
                            subject,
                            UtxoStatus.Unspent
                        );
                    });
            })
            .Where(e => e != null)
            .Select(e => e!)
            .ToList();

        dbContext.PriceByToken.AddRange(jpgPriceBySubjects);
    }

    public async Task<ulong?> QueryTip()
    {
        using T dbContext = await dbContextFactory.CreateDbContextAsync();
        ulong maxSlot = await dbContext.PriceByToken.MaxAsync(x => (ulong?)x.Slot) ?? 0;
        return maxSlot;
    }
}