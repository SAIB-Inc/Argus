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
using Argus.Sync.Utils;

namespace Argus.Sync.Example.Reducers;

public class SundaePriceByTokenReducer(
    IDbContextFactory<TestDbContext> dbContextFactory
) 
: IReducer<PriceByToken>
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
                string.Empty,
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

                        byte[]? datum = o.Datum();

                        if (datum is null) return [];

                        SundaeSwapLiquidityPool? liquidityPool = CborSerializer.Deserialize<SundaeSwapLiquidityPool>(datum ?? []) ?? null;

                        if (liquidityPool is null) return [];

                        AssetClassTuple assets = liquidityPool.Assets;

                        AssetClass? tokenX = assets.AssetX;
                        string tokenXPolicy = Convert.ToHexStringLower(tokenX.PolicyId);
                        string tokenXName = Convert.ToHexStringLower(tokenX.AssetName);

                        AssetClass? tokenY = assets.AssetY;
                        string tokenYPolicy = Convert.ToHexStringLower(tokenY.PolicyId);
                        string tokenYName = Convert.ToHexStringLower(tokenY.AssetName);

                        if (tokenXPolicy == string.Empty || tokenYPolicy == string.Empty)
                        {
                            Value? outputAmount = o.Amount();
                            if (outputAmount is null) return [];

                            ulong adaReserve = outputAmount.Lovelace();

                            // if reserve is less than 10k ada, skip
                            if (adaReserve < 10_000 && excludeLowReserves) return [];

                            string otherTokenPolicy =
                                tokenXPolicy == string.Empty ? tokenXPolicy : tokenYPolicy;

                            string otherTokenName =
                                tokenYName == string.Empty ? tokenYName : tokenXName;

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
}



public record TokenIdentifier(string PolicyId, string AssetName);