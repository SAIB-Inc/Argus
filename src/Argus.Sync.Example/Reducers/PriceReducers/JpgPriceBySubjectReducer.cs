using System.Linq.Expressions;
using System.Text;
using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Example.Extensions;
using Argus.Sync.Example.Models;
using Argus.Sync.Example.Models.Cardano.Jpeg.Datums;
using Argus.Sync.Example.Models.Enums;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers.PriceReducers;

public class JpgPriceBySubjectReducer(
    IDbContextFactory<TestDbContext> dbContextFactory,
    IConfiguration configuration
) : TokenPriceBaseReducer(configuration), IReducer<PriceByToken>
{
    private readonly double _jpgFee = 0.02;
    private readonly ulong _jpgMinFee = 1000000;

    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        List<PriceBySubject> jpgListingEntriesToDelist = await dbContext.PricesBySubject
            .Where(e => e.SpentSlot >= slot)
            .ToListAsync();

        if (!jpgListingEntriesToDelist.Any()) return;

        jpgListingEntriesToDelist.ForEach(e =>
        {
            PriceBySubject entryToUpdate = e with
            {
                SpentSlot = null,
                Status = UtxoStatus.Unspent
            };

            dbContext.PricesBySubject.Update(entryToUpdate);
        });

        dbContext.PricesByToken.RemoveRange(
            dbContext.PricesByToken
                .Where(e => e.Slot >= slot)
        );

        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();
        if (!transactions.Any()) return;

        ulong slot = block.Header().HeaderBody().Slot();
        ProcessOutputs(block, transactions, slot, dbContext);

        List<string> inputOutRefs = [.. block.TransactionBodies()
            .SelectMany(
                tx => tx.Inputs(),
                (_, input) => Convert.ToHexString(input.TransactionId).ToLowerInvariant() + input.Index.ToString()
            )];

        Expression<Func<PriceBySubject, bool>> predicate = PredicateBuilder.False<PriceBySubject>();

        inputOutRefs.ForEach(inputTuple =>
        {
            predicate = predicate.Or(jpg => jpg.OutRef == inputTuple && jpg.Status == UtxoStatus.Unspent);
        });

        List<PriceBySubject> jpgListingEntries = await dbContext.PricesBySubject
            .Where(predicate)
            .ToListAsync();

        List<PriceBySubject> jpgListingEntriesLocal = [.. dbContext.PricesBySubject.Local.Where(e => inputOutRefs.Contains(e.OutRef) && e.Status == UtxoStatus.Unspent)];
        jpgListingEntries.AddRange(jpgListingEntriesLocal);

        ProcessInputs(jpgListingEntries, slot, dbContext);
        await dbContext.SaveChangesAsync();
    }

    private void ProcessOutputs(Block block, IEnumerable<TransactionBody> txBodies, ulong slot, TestDbContext dbContext)
    {
        List<PriceBySubject> jpgPriceBySubjects = [.. txBodies
            .SelectMany((tx, txIndexInBlock) =>
            {
                string txHash = tx.Hash();

                return tx.Outputs().SelectMany(output =>
                {
                    if (!TryExtractAddressHash(output, out string? outputAddressPkh) ||
                                outputAddressPkh != JpegV1ScriptHash) return [];

                    int? cborTxIndexInBlock = block.AuxiliaryDataSet()
                        .Keys
                        .FirstOrDefault(key => key == txIndexInBlock);

                    if (cborTxIndexInBlock is null) return [];

                    Dictionary<string, byte[]> metadata =
                        cborTxIndexInBlock is not null
                            ? MapMetadataToDatumDictionary(block.AuxiliaryDataSet()[cborTxIndexInBlock.Value])
                            : [];

                    return tx.Outputs()
                        .Select((o, txIndex) =>
                        {
                            if (!TryExtractAddressHash(output, out string? outputAddressPkh) ||
                                outputAddressPkh != JpegV1ScriptHash) return null;
                            
                            (DatumType DatumType, byte[]? RawData) datumInfo = o.DatumInfo();

                            if (datumInfo.RawData is null || datumInfo.DatumType == DatumType.None) return null;

                            byte[]? datum = null;

                            if (datumInfo.DatumType is DatumType.Hash && metadata.TryGetValue(Convert.ToHexString(datumInfo.RawData ?? []), out byte[]? rawDatum) && rawDatum != null)
                            {
                                datum = o.Datum();
                            }

                            Listing? listing = CborSerializer.Deserialize<Listing>(datum ?? []);
                            if (listing is null) return null;

                            Value outputAmount = o.Amount();
                            Dictionary<byte[], TokenBundleOutput>? multiAssetOutput = outputAmount.MultiAsset();
                            if (multiAssetOutput is null) return null;

                            ulong totalPayoutAmount = listing.Payouts.Value
                                .Aggregate(0UL, (acc, payout) => acc + payout.Amount);

                            string subject = multiAssetOutput
                                .SelectMany(v => v.Value.Value
                                    .Select(tokenBundle => 
                                        Convert.ToHexString(v.Key).ToLowerInvariant() + 
                                        Convert.ToHexString(tokenBundle.Key).ToLowerInvariant()))
                                .First()
                                .ToLowerInvariant();

                            ulong price = ((ulong)(totalPayoutAmount * _jpgFee)) > _jpgMinFee
                                ? (ulong)(totalPayoutAmount * (1 + _jpgFee))
                                : (totalPayoutAmount + _jpgMinFee);

                            return new PriceBySubject(
                                $"{txHash}{txIndex}",
                                slot,
                                null,
                                subject,
                                price,
                                UtxoStatus.Unspent
                            );
                        });
                });
            })
            .Where(e => e is not null)];

        dbContext.PricesBySubject.AddRange(jpgPriceBySubjects);
    }

    private static void ProcessInputs(List<PriceBySubject> jpgListingEntries, ulong slot, TestDbContext dbContext)
    {
        jpgListingEntries.ForEach(e =>
        {
            PriceBySubject entryToUpdate = e with
            {
                SpentSlot = slot,
                Status = UtxoStatus.Spent
            };

            dbContext.PricesBySubject.Update(entryToUpdate);
        });
    }

    private static Dictionary<string, byte[]> MapMetadataToDatumDictionary(AuxiliaryData data)
    {
        Dictionary<string, byte[]> datumDict = [];

        StringBuilder datumBuild = new();

        data.Metadata()?.Value
            .Where(metaDict => metaDict.Key != 30)
            .ToList()
            .ForEach(metaDict =>
            {
                if (metaDict.Value is null) return;

                object value = metaDict.Value;
                if (value is string strValue) datumBuild.Append(strValue);
            });

        string datumHex = datumBuild.ToString().TrimEnd(',');
        string[] datumArr = datumHex.Split(',');

        datumDict = datumArr
            .Select(Convert.FromHexString)
            .ToArray()
            .DistinctBy(datum => Convert.ToHexString(Chrysalis.Wallet.Utils.HashUtil.Blake2b256(datum)))
            .ToDictionary(datum => Convert.ToHexString(Chrysalis.Wallet.Utils.HashUtil.Blake2b256(datum)), datum => datum);

        return datumDict;
    }
}