using System.Linq.Expressions;
using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Extensions;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Block = Chrysalis.Cardano.Models.Core.Block.Block;
using JpegListing = Chrysalis.Cardano.Models.Jpeg.Listing;
using JpegOffer = Chrysalis.Cardano.Models.Jpeg.Offer;
using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cardano.Models.Core.Transaction;

namespace Argus.Sync.Reducers;

public class JpegPriceBySubjectReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<JpegPriceBySubject> where T : CardanoDbContext
{
    private T _dbContext = default!;
    private readonly string _jpegV1validatorPkh = configuration.GetValue<string>("JPGStoreMarketplaceV1ValidatorScriptHash")!;
    private readonly string _jpegV2validatorPkh = configuration.GetValue<string>("JPGStoreMarketplaceV1ValidatorScriptHash")!;

    public async Task RollBackwardAsync(ulong slot)
    {
        _dbContext = dbContextFactory.CreateDbContext();

        IQueryable<JpegPriceBySubject> rollbackEntries = _dbContext.JpegPriceBySubjects.AsNoTracking().Where(jpg => jpg.Slot >= slot);
        _dbContext.JpegPriceBySubjects.RemoveRange(rollbackEntries);

        await _dbContext.SaveChangesAsync();
        _dbContext.Dispose();
    }

    public async Task RollForwardAsync(Block block)
    {
        if (block.TransactionBodies().Count() != 0)
        {
            _dbContext = dbContextFactory.CreateDbContext();

            await ProcessInputsAsync(block);
            ProcessOutputs(block);

            await _dbContext.SaveChangesAsync();
            _dbContext.Dispose();
        }
    }

    private async Task ProcessInputsAsync(Block block)
    {
        IEnumerable<TransactionBody> transactionBodies = block.TransactionBodies();

        List<(string, string)> inputHashes = [];

        foreach (TransactionBody txBody in transactionBodies)
        {
            IEnumerable<TransactionInput> transactionInputs = txBody.Inputs();

            inputHashes.AddRange(transactionInputs
                .Select(input => (Convert.ToHexString(input.TransactionId.Value).ToLowerInvariant(), input.Index.Value.ToString()))
                .ToList());
        }
        
        Expression<Func<JpegPriceBySubject, bool>> predicate = PredicateBuilder.False<JpegPriceBySubject>();

        foreach ((string txHash, string txIndex) in inputHashes)
        {
            predicate = predicate.Or(jpg => jpg.TxHash == txHash && jpg.TxIndex.ToString() == txIndex);
        }

        List<JpegPriceBySubject> existingOutputs = await _dbContext.JpegPriceBySubjects
            .Where(predicate)
            .ToListAsync();

        if (existingOutputs.Any())
        {
            existingOutputs.ForEach(jpg =>
            {
                jpg.Status = UtxoStatus.Spent;
            });

            _dbContext.JpegPriceBySubjects.UpdateRange(existingOutputs);
        }
    }

    private void ProcessOutputs(Block block)
    {
        List<JpegPriceBySubject?> jpegPriceBySubjects = block.TransactionBodies()
            .SelectMany(txBody => txBody.Outputs()
                .Select((output, outputIndex) =>
                {
                    string? outputAddressPkh = Convert.ToHexString(output.Address().GetPublicKeyHash()).ToLowerInvariant();
                    
                    if (outputAddressPkh == null || (outputAddressPkh != _jpegV1validatorPkh && outputAddressPkh != _jpegV2validatorPkh)) return null;

                    Datum? datum = output.GetDatumInfo() is var datumInfo && datumInfo.HasValue
                        ? new Datum(datumInfo.Value.Type, datumInfo.Value.Value)
                        : null;

                    if (datum is null) return null;

                    Value outputAmount = output.Amount();
                    MultiAssetOutput? multiAssetOutput = outputAmount.MultiAsset();

                    if (multiAssetOutput is null) return null;

                    string subject = multiAssetOutput.GetSubject().ToLowerInvariant();
                    ulong outputCoin = outputAmount.GetCoin();

                    JpegPriceBySubject jpegPriceBySubject = new()
                    {
                        Slot = block.Number(),
                        TxHash = Convert.ToHexString(CborSerializer.Serialize(txBody).ToBlake2b()).ToLowerInvariant(),
                        TxIndex = (ulong)outputIndex,
                        Subject = subject,
                        Status = UtxoStatus.Unspent
                    };

                    if (outputAddressPkh == _jpegV1validatorPkh)
                    {
                        JpegListing? listing = CborSerializer.Deserialize<JpegListing>(datum.Data);

                        if (listing is null) return null;

                        decimal totalPayoutAmount = listing.Payouts.Value
                            .Sum(jpgPayout => (decimal)jpgPayout.Amount.Value);
    
                        jpegPriceBySubject.Price = (ulong)totalPayoutAmount + outputCoin;
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

                        jpegPriceBySubject.Price = (ulong)totalPayoutAmount + outputCoin;
                    }

                    return jpegPriceBySubject;
                }))
            .ToList();

        jpegPriceBySubjects.ForEach(jpegPriceBySubject => 
        {
            if (jpegPriceBySubject is null) return;

            _dbContext.JpegPriceBySubjects.Add(jpegPriceBySubject);
        });
    }
}