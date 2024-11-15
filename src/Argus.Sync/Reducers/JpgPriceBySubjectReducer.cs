using Chrysalis.Cbor;
using System.Linq.Expressions;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Extensions;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Argus.Sync.Data.Models.Jpg;
using Block = Chrysalis.Cardano.Core.Block;
using JpgListing = Argus.Sync.Data.Models.Jpg.Listing;
using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;
using Chrysalis.Utils;

namespace Argus.Sync.Reducers;

public class JpgPriceByTokenReducer<T>(
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

        /*Expression<Func<PriceByToken, bool>> predicate = PredicateBuilder.False<PriceByToken>();

        inputsTuple.ForEach(inputTuple =>
        {
            predicate = predicate.Or(jpg => jpg.TxHash == inputTuple.TxHash && jpg.TxIndex.ToString() == inputTuple.TxIndex);
        });

        List<PriceByToken> jpgListingEntries = await dbContext.PriceByToken
            .Where(predicate)
            .ToListAsync();*/

        List<PriceByToken> jpgListingEntries = await dbContext.PriceByToken
            .Where(jpg => inputsTuple.Any(inputTuple => 
            jpg.TxHash == inputTuple.TxHash && jpg.TxIndex.ToString() == inputTuple.TxIndex))
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

                CborInt? cborTxIndexInBlock = block.AuxiliaryDataSet.Value.Keys
                    .FirstOrDefault(key => key.Value == txIndexInBlock);

                Dictionary<string, byte[]> metadata =
                    cborTxIndexInBlock is not null
                        ? JpgUtils.MapMetadataToDatumDictionary(block.AuxiliaryDataSet.Value[cborTxIndexInBlock])
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

                        (DatumType Type, byte[] Data)? datumInfo = o.ArgusDatumInfo();
                        if (datumInfo is null) return null;

                        Datum datum = new(datumInfo.Value.Type, datumInfo.Value.Data);

                        if (
                            datum.Type is DatumType.DatumHash &&
                            metadata.TryGetValue(Convert.ToHexString(datum.Data), out byte[]? rawDatum) &&
                            rawDatum != null
                        )
                        {
                            datum = new (DatumType.InlineDatum, rawDatum);
                        }

                        JpgListing? listing = CborSerializer.Deserialize<JpgListing>(datum!.Data);
                        if (listing is null) return null;

                        Value outputAmount = o.Amount()!;
                        MultiAssetOutput? multiAssetOutput = outputAmount!.MultiAsset();
                        if (multiAssetOutput is null) return null;

                        ulong totalPayoutAmount = listing.Payouts.Value
                            .Aggregate(0UL,(acc, payout) => acc + payout.Amount.Value);

                        string subject = multiAssetOutput.GetSubject().ToLowerInvariant();

                        double jpgFee = 0.02;
                        ulong jpgMinFee = 1000000;
                        ulong price = ((ulong)(totalPayoutAmount * jpgFee)) > jpgMinFee
                            ? (ulong)(totalPayoutAmount * (1 + jpgFee)) 
                            : (totalPayoutAmount + jpgMinFee);

                        return new PriceByToken(
                            block.Slot(),
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
}