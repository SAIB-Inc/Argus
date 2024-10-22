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
using Argus.Sync.Data.Models.Jpeg;
using Chrysalis.Cardano.Models.Core.Block.Transaction;
using Chrysalis.Cardano.Models.Core.Block.Transaction.Output;
using Block = Chrysalis.Cardano.Models.Core.BlockEntity;
using JpegListing = Chrysalis.Cardano.Models.Jpeg.Listing;
using JpegOffer = Chrysalis.Cardano.Models.Jpeg.Offer;


namespace Argus.Sync.Reducers;

public class JpegPriceByTokenReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<PriceByToken> where T : JpegPriceByTokenDbContext, IJpegPriceByTokenDbContext
{
    private readonly string _jpegV1validatorPkh = configuration.GetValue<string>("JPGStoreMarketplaceV1ValidatorScriptHash")!;
    private readonly string _jpegV2validatorPkh = configuration.GetValue<string>("JPGStoreMarketplaceV1ValidatorScriptHash")!;

    public async Task RollBackwardAsync(ulong slot)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<PriceByToken> rollbackEntries = dbContext.PriceByToken.AsNoTracking().Where(jpg => jpg.Slot >= slot);
        dbContext.PriceByToken.RemoveRange(rollbackEntries);

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        if (block.TransactionBodies().Count() != 0)
        {
            await using T dbContext = await dbContextFactory.CreateDbContextAsync();
            IEnumerable<TransactionBody> transactionBodies = block.TransactionBodies();

            List<(string, string)> inputHashes = [];

            foreach (TransactionBody txBody in transactionBodies)
            {
                IEnumerable<TransactionInput> transactionInputs = txBody.Inputs();

                inputHashes.AddRange(transactionInputs
                    .Select(input => (Convert.ToHexString(input.TransactionId.Value).ToLowerInvariant(), input.Index.Value.ToString()))
                    .ToList());
            }

            Expression<Func<PriceByToken, bool>> predicate = PredicateBuilder.False<PriceByToken>();

            foreach ((string txHash, string txIndex) in inputHashes)
            {
                predicate = predicate.Or(jpg => jpg.TxHash == txHash && jpg.TxIndex.ToString() == txIndex);
            }

            List<PriceByToken> existingOutputs = await dbContext.PriceByToken
                .Where(predicate)
                .ToListAsync();

            ProcessInputs(existingOutputs, dbContext);
            ProcessOutputs(block, dbContext);

            await dbContext.SaveChangesAsync();
            dbContext.Dispose();
        }
    }

    private void ProcessInputs(List<PriceByToken> existingOutputs, T dbContext)
    {
        if (existingOutputs.Any())
        {
            existingOutputs.ForEach(jpg =>
            {
                jpg.Status = UtxoStatus.Spent;
            });

            dbContext.PriceByToken.UpdateRange(existingOutputs);
        }
    }

    private void ProcessOutputs(Block block, T dbContext)
    {
        List<PriceByToken?> jpegPriceBySubjects = block.TransactionBodies()
            .SelectMany(txBody => txBody.Outputs()
                .Select((output, outputIndex) =>
                {
                    string? outputAddressPkh = Convert.ToHexString(output.Address().GetPublicKeyHash()).ToLowerInvariant();

                    if (outputAddressPkh == null || (outputAddressPkh != _jpegV1validatorPkh && outputAddressPkh != _jpegV2validatorPkh)) return null;

                    Datum? datum = output.DatumInfo() is var datumInfo && datumInfo.HasValue
                        ? new Datum(datumInfo.Value.Type, datumInfo.Value.Data)
                        : null;

                    if (datum is null) return null;

                    Value outputAmount = output.Amount();
                    MultiAssetOutput? multiAssetOutput = outputAmount.MultiAsset();

                    if (multiAssetOutput is null) return null;

                    string subject = multiAssetOutput.GetSubject().ToLowerInvariant();
                    ulong outputCoin = outputAmount.GetCoin();
                    ulong price = 0;
                    if (outputAddressPkh == _jpegV1validatorPkh)
                    {
                        JpegListing? listing = CborSerializer.Deserialize<JpegListing>(datum.Data);

                        if (listing is null) return null;

                        decimal totalPayoutAmount = listing.Payouts.Value
                            .Sum(jpgPayout => (decimal)jpgPayout.Amount.Value);

                        price = (ulong)totalPayoutAmount + outputCoin;
                    }
                    else if (outputAddressPkh == _jpegV2validatorPkh)
                    {
                        JpegOffer? offer = CborSerializer.Deserialize<JpegOffer>(datum.Data);

                        if (offer is null) return null;

                        decimal totalPayoutAmount = offer.Payouts.Value
                            .Sum(jpgPayout => jpgPayout.PayoutValue.Value
                                .Select(token => token.Value.Amount.Value
                                    .Select(v => (decimal)v.Value.Value)
                                    .First())
                                .First()
                            );

                        price = (ulong)totalPayoutAmount + outputCoin;
                    }

                    return new PriceByToken(
                        block.Slot(),
                        txBody.Id(),
                        (ulong)outputIndex,
                        price,
                        subject,
                        UtxoStatus.Unspent
                    );
                }))
            .ToList();

        jpegPriceBySubjects.ForEach(jpegPriceBySubject =>
        {
            if (jpegPriceBySubject is null) return;

            dbContext.PriceByToken.Add(jpegPriceBySubject);
        });
    }
}