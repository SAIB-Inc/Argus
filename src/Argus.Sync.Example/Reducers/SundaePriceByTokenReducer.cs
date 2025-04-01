using Argus.Sync.Example.Models;
using Argus.Sync.Example.Models.Enums;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;
using Block = Chrysalis.Cbor.Types.Cardano.Core.Block;
using Argus.Sync.Reducers;
using Argus.Sync.Example.Extensions;
using Argus.Sync.Example.Models.Cardano.Sundae;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;

namespace Argus.Sync.Example.Reducers;

public class SundaePriceByTokenReducer(
    IDbContextFactory<TestDbContext> dbContextFactory
)
{
    private readonly string SundaeSwapScriptHash = "e0302560ced2fdcbfcb2602697df970cd0d6a38f94b32703f51c312b";

    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<PriceByToken> rollbackTokenEntries = dbContext.PricesByToken
            .AsNoTracking()
            .Where(b => b.Slot >= slot && b.PlatformType == TokenPricePlatformType.SundaeSwap);

        dbContext.PricesByToken.RemoveRange(rollbackTokenEntries);
        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();
        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        Dictionary<string, (string outRef, ulong adaReserve, ulong otherTokenReserve)> tokenPricesDict =
           ProcessOutputs(transactions);

        if (!tokenPricesDict.Any()) return;

        ulong slot = block.Header().HeaderBody().Slot();

        tokenPricesDict.ToList().ForEach(tp =>
        {
            string subject = tp.Key;

            PriceByToken tokenPriceHistory = new(
                tp.Value.outRef,
                slot,
                string.Empty, // ADA always
                subject,
                tp.Value.adaReserve,
                tp.Value.otherTokenReserve,
                TokenPricePlatformType.SundaeSwap
            );

            dbContext.PricesByToken.Add(tokenPriceHistory);
        });

        await dbContext.SaveChangesAsync();
    }

    protected Dictionary<string, (string outRef, ulong adaReserve, ulong otherTokenReserve)> ProcessOutputs(
        IEnumerable<TransactionBody> transactions,
        bool excludeLowReserves = false
    )
    {
        return transactions
            .SelectMany((tx, txIndex) =>
            {
                string txHash = tx.Hash();
                return tx.Outputs()
                    .SelectMany(o =>
                    {
                        string? outputAddressPkh = Convert.ToHexStringLower(new WalletAddress(o.Address()).GetPaymentKeyHash() ?? []);
                        if (string.IsNullOrEmpty(outputAddressPkh) || outputAddressPkh != SundaeSwapScriptHash) return [];

                        (DatumType Type, byte[]? Data)? datumInfo = o.DatumInfo();

                        if (!datumInfo.HasValue) return [];

                        SundaeSwapLiquidityPool? liquidityPool = CborSerializer.Deserialize<SundaeSwapLiquidityPool>(datumInfo.Value.Data ?? []) ?? null;

                        if (liquidityPool is null) return [];

                        AssetClassTuple assets = liquidityPool.Assets;

                        (string TokenPolicy, string TokenName) assetX = GetTokenTuple(assets, 0);
                        (string TokenPolicy, string TokenName) assetY = GetTokenTuple(assets, 1);

                        if (assetX.TokenPolicy == string.Empty || assetY.TokenPolicy == string.Empty)
                        {
                            Value? outputAmount = o.Amount();
                            if (outputAmount is null) return [];

                            ulong adaReserve = outputAmount.Lovelace();

                            // if reserve is less than 10k ada, skip
                            if (adaReserve < 10_000 && excludeLowReserves) return [];

                            string otherTokenPolicy =
                                assetX.TokenPolicy == string.Empty ? assetY.TokenPolicy : assetX.TokenPolicy;

                            string otherTokenName =
                                assetX.TokenName == string.Empty ? assetY.TokenName : assetX.TokenName;

                            ulong otherTokenReserve = o.Amount().QuantityOf(otherTokenPolicy + otherTokenName) ?? 0UL;

                            string otherTokenSubject = otherTokenPolicy + otherTokenName;
                            string outRef = txHash + txIndex;

                            return new[] { (otherTokenSubject, (outRef, adaReserve, otherTokenReserve)) };
                        }

                        return [];
                    });
            })
            .Where(kvp => !string.IsNullOrEmpty(kvp.otherTokenSubject))
            .Select(tpd => tpd)
            .GroupBy(x => x.otherTokenSubject)
            .Select(g => g.First())
            .ToDictionary();
    }

    private static (string TokenPolicy, string TokenName) GetTokenTuple(AssetClassTuple assetClass, int index)
    {
        if (index < 0 || index >= assetClass.AssetClassTuple()?.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Invalid index {index} for AssetClassTuple.");
        }

        AssetClass? tokenDetails = assetClass.AssetClassTuple()?[index].AssetClass();
        string tokenPolicy = Convert.ToHexStringLower(tokenDetails?.AssetClassValue()?[0].Value!);
        string tokenName = Convert.ToHexStringLower(tokenDetails?.AssetClassValue()?[1].Value!);

        return (tokenPolicy, tokenName);
    }
}



public record TokenIdentifier(string PolicyId, string AssetName);