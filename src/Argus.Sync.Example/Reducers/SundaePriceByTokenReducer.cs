using System.Linq.Expressions;
using System.Text;
using Argus.Sync.Example.Extensions;
using Argus.Sync.Example.Models;
using Argus.Sync.Example.Models.Cardano.Sundae;
using Argus.Sync.Example.Utils;
using Argus.Sync.Reducers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Argus.Sync.Example.Reducers;

public class SundaeSwapReducer(
    IDbContextFactory<TestDbContext> dbContextFactory
) : IReducer<SundaePriceByToken>
{
    private readonly string _sundaeScriptHash = "e0302560ced2fdcbfcb2602697df970cd0d6a38f94b32703f51c312b";

    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<SundaePriceByToken> entriesToRemove = dbContext.SundaePricesByToken
            .AsNoTracking()
            .Where(t => t.Slot >= slot && t.SlotUpdated == null);

        IQueryable<SundaePriceByToken> entriesToUpdate = dbContext.SundaePricesByToken
            .AsNoTracking()
            .Where(t => t.SlotUpdated >= slot);

        var rollbackOutRefs = entriesToUpdate
            .Select(t => new { t.TxHash, t.TxIndex });

        dbContext.SundaePricesByToken.RemoveRange(entriesToRemove);
        dbContext.SundaePricesByToken.RemoveRange(entriesToUpdate);

        // This is to prevent a potential stack overflow
        bool hasMoreEntries = true;
        int processedCount = 0;

        while (hasMoreEntries)
        {
            var entriesBatch = await rollbackOutRefs
                .Skip(processedCount)
                .Take(200) // TODO - make this configurable
                .ToListAsync();

            Expression<Func<OutputBySlot, bool>> predicate = PredicateBuilder.False<OutputBySlot>();
            entriesBatch.ForEach(outRef =>
                predicate = predicate.Or(o => o.TxHash == outRef.TxHash && o.TxIndex == outRef.TxIndex)
            );

            List<OutputBySlot> matchingOutputs = await dbContext.OutputsBySlot
                .Where(o => o.Slot < slot)
                .Where(predicate)
                .ToListAsync();

            matchingOutputs.ForEach(output =>
            {
                try
                {
                    TransactionOutput txOutput = CborSerializer.Deserialize<TransactionOutput>(output.OutputRaw);
                    ProcessSingleOutput(txOutput, output.TxHash, output.TxIndex, output.Slot, dbContext);
                }
                catch
                {
                    return;
                }
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        ulong slot = block.Header().HeaderBody().Slot();
        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        transactions.ToList().ForEach(tx => ProcessOutputs(tx, slot, dbContext));

        List<SundaePriceByToken> localEntries = [.. dbContext.SundaePricesByToken.Local];

        Expression<Func<SundaePriceByToken, bool>> tokenPricePredicate = PredicateBuilder.False<SundaePriceByToken>();
        localEntries.ToList().ForEach(tp =>
            tokenPricePredicate = tokenPricePredicate.Or(hp => hp.AssetY == tp.AssetY && hp.AssetX == tp.AssetX)
        );

        List<SundaePriceByToken> existingTokenPrices = await dbContext.SundaePricesByToken
            .AsNoTracking()
            .Where(tokenPricePredicate)
            .ToListAsync();

        ProcessExistingTokens(existingTokenPrices, slot, dbContext);
        await dbContext.SaveChangesAsync();
    }

    protected void ProcessOutputs(
        TransactionBody tx,
        ulong slot,
        TestDbContext dbContext
    )
    {
        string txHash = tx.Hash();

        tx.Outputs()
            .Select((output, index) => new { Output = output, OutputIndex = (ulong)index })
            .ToList()
            .ForEach(e =>
                ProcessSingleOutput(
                    e.Output,
                    txHash,
                    e.OutputIndex,
                    slot,
                    dbContext
                )
            );
    }

    private void ProcessSingleOutput(
        TransactionOutput output,
        string txHash,
        ulong outputIndex,
        ulong slot,
        TestDbContext dbContext)
    {
        if (!DatumUtils.TryGetWalletAddress(output, out WalletAddress? wallet)) return;

        string? outputAddressPkh = Convert.ToHexStringLower(wallet.GetPaymentKeyHash() ?? []);
        if (string.IsNullOrEmpty(outputAddressPkh) || outputAddressPkh != _sundaeScriptHash) return;

        if (!TryDeserializeDatum(output, out SundaeSwapLiquidityPoolDatum liquidityPool)) return;

        AssetClassTuple assets = liquidityPool.Assets;
        AssetClass assetX = assets.AssetX;
        AssetClass assetY = assets.AssetY;

        if (assetX.PolicyId() == string.Empty || assetY.PolicyId() == string.Empty)
        {
            ulong adaReserve = output.Amount().Lovelace() / 1_000_000;
            if (adaReserve < 10_000) return;

            string otherTokenPolicy = assetX.PolicyId() == string.Empty ? assetY.PolicyId() : assetX.PolicyId();
            string otherTokenName = assetX.AssetName() == string.Empty ? assetY.AssetName() : assetX.AssetName();

            ulong otherTokenReserve = output.Amount().QuantityOf(otherTokenPolicy + otherTokenName) ?? 0UL;

            SundaePriceByToken newEntry = new(
                slot,
                txHash,
                outputIndex,
                Convert.ToHexString(liquidityPool.Identifier).ToLowerInvariant(),
                assetX.PolicyId() == string.Empty ? "ada" : $"{assetX.PolicyId()}.{assetX.AssetName()}",
                assetY.PolicyId() == string.Empty ? "ada" : $"{assetY.PolicyId()}.{assetY.AssetName()}",
                adaReserve,
                otherTokenReserve,
                $"{GetSafeAssetName(assetX.PolicyId(), assetX.AssetName())}/{GetSafeAssetName(assetY.PolicyId(), assetY.AssetName())}",
                Convert.ToHexString(liquidityPool.Identifier).ToLowerInvariant(),
                liquidityPool.CirculatingLp,
                null
            );

            SundaePriceByToken? existingEntry = dbContext.SundaePricesByToken.Local
                .FirstOrDefault(e => e.AssetX == newEntry.AssetX && e.AssetY == newEntry.AssetY);

            if (existingEntry != null)
            {
                SundaePriceByToken updatedEntry = existingEntry with
                {
                    Slot = slot,
                    AssetXPrice = newEntry.AssetXPrice,
                    AssetYPrice = newEntry.AssetYPrice,
                    SlotUpdated = slot
                };

                dbContext.SundaePricesByToken.Remove(existingEntry);
                dbContext.SundaePricesByToken.Add(updatedEntry);
            }
            else
            {
                dbContext.SundaePricesByToken.Add(newEntry);
            }
        }
    }

    private static void ProcessExistingTokens(List<SundaePriceByToken> existingTokenPrices, ulong slot, TestDbContext dbContext)
    {
        existingTokenPrices.ForEach(existingTokenPrice =>
        {
            SundaePriceByToken? existingEntry = existingTokenPrices
                .FirstOrDefault(e => e.AssetX == existingTokenPrice.AssetX && e.AssetY == existingTokenPrice.AssetY);

            existingEntry ??= dbContext.SundaePricesByToken.Local
                .FirstOrDefault(e => e.AssetX == existingTokenPrice.AssetX && e.AssetY == existingTokenPrice.AssetY);

            if (existingEntry is null)
            {
                dbContext.SundaePricesByToken.Add(existingTokenPrice);
                return;
            }

            SundaePriceByToken newEntry = existingTokenPrice with
            {
                Slot = slot,
                AssetXPrice = existingTokenPrice.AssetXPrice,
                AssetYPrice = existingTokenPrice.AssetYPrice,
                SlotUpdated = slot
            };

            dbContext.SundaePricesByToken.Remove(existingEntry);
            dbContext.SundaePricesByToken.Add(newEntry);
        });
    }

    private bool TryDeserializeDatum(in TransactionOutput txOut, out SundaeSwapLiquidityPoolDatum datum)
    {
        datum = default!;

        try
        {
            WalletAddress addr = new(txOut.Address());
            byte[] keyHashBytes = addr.GetPaymentKeyHash() ?? [];
            string pkh = Convert.ToHexString(keyHashBytes).ToLowerInvariant();

            if (pkh != _sundaeScriptHash)
                return false;

            DatumOption? datumOption = txOut.DatumOption();
            if (datumOption == null)
                return false;

            CborEncodedValue inlineDatum = new CborEncodedValue(datumOption.Data());
            datum = CborSerializer.Deserialize<SundaeSwapLiquidityPoolDatum>(inlineDatum.GetValue());
            return datum is not null;
        }
        catch
        {
            return false;
        }
    }

    private static string GetSafeAssetName(string policyId, string assetName)
    {
        if (string.IsNullOrEmpty(policyId)) return "ada";

        try
        {
            byte[] bytes = Convert.FromHexString(assetName);
            string decoded = Encoding.UTF8.GetString(bytes);

            string sanitized = new([.. decoded.Where(c =>
                !char.IsControl(c) &&
                c != '�' &&
                c > 31 &&
                c < 127)]
            );

            return string.IsNullOrWhiteSpace(sanitized)
                ? $"{policyId}.{assetName}"
                : sanitized.ToLower();
        }
        catch
        {
            return $"{policyId}.{assetName}";
        }
    }
}