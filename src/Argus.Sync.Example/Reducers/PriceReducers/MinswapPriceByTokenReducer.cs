using Argus.Sync.Example.Extensions;
using Argus.Sync.Example.Models;
using Argus.Sync.Example.Models.Cardano.Minswap;
using Argus.Sync.Example.Models.Enums;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers.PriceReducers;

public class MinswapPriceByTokenReducer(
    IDbContextFactory<TestDbContext> dbContextFactory,
    IConfiguration configuration
) : TokenPriceBaseReducer(configuration)
{
    public async Task RollBackwardAsync(ulong slot)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IQueryable<PriceByToken> rollbackTokenEntries = dbContext.PricesByToken
            .AsNoTracking()
            .Where(b => b.Slot >= slot && b.PlatformType == TokenPricePlatformType.Minswap);

        dbContext.PricesByToken.RemoveRange(rollbackTokenEntries);
        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();
        if (!transactions.Any()) return;

        Dictionary<string, (string outRef, ulong adaReserve, ulong otherTokenReserve)> tokenPricesDict =
           ProcessOutputs(transactions);

        if (!tokenPricesDict.Any()) return;

        ulong slot = block.Header().HeaderBody().Slot();
        tokenPricesDict.ToList().ForEach(tp =>
        {
            string subject = tp.Key;

            PriceByToken tokenPriceHistory = new(
                OutRef: tp.Value.outRef.ToLowerInvariant(),
                Slot: slot,
                TokenXSubject: string.Empty,
                TokenYSubject: subject,
                TokenXPrice: tp.Value.adaReserve,
                TokenYPrice: tp.Value.otherTokenReserve,
                PlatformType: TokenPricePlatformType.Minswap
            );

            dbContext.PricesByToken.Add(tokenPriceHistory);
        });

        await dbContext.SaveChangesAsync();
    }

    protected Dictionary<string, (string outRef, ulong adaReserve, ulong otherTokenReserve)> ProcessOutputs(
        IEnumerable<TransactionBody> transactions)
    {
        return transactions
            .SelectMany((tx, txIndex) =>
            {
                string txHash = tx.Hash();

                return tx.Outputs().SelectMany(o =>
                {
                    if (!TryExtractAddressHash(o, out string? outputAddressPkh) ||
                        outputAddressPkh != MinswapScriptHash) return [];

                    if (!TryDeserializeLiquidityPool(o.Datum(), out MinswapLiquidityPool liquidityPool)) return [];

                    (string tokenXPolicy, string tokenXName) =
                        (Convert.ToHexStringLower(liquidityPool.AssetX.PolicyId), Convert.ToHexStringLower(liquidityPool.AssetX.AssetName));

                    (string tokenYPolicy, string tokenYName) =
                        (Convert.ToHexStringLower(liquidityPool.AssetY.PolicyId), Convert.ToHexStringLower(liquidityPool.AssetY.AssetName));

                    if (!string.IsNullOrEmpty(tokenXPolicy) && !string.IsNullOrEmpty(tokenYPolicy)) return [];

                    Value outputAmount = o.Amount();
                    ulong adaReserve = outputAmount.Lovelace();

                    if (adaReserve < 10_000) return [];

                    string otherTokenPolicy = string.IsNullOrEmpty(tokenXPolicy) ? tokenYPolicy : tokenXPolicy;
                    string otherTokenName = string.IsNullOrEmpty(tokenXName) ? tokenYName : tokenXName;

                    ulong otherTokenReserve = outputAmount.QuantityOf(otherTokenPolicy + otherTokenName) ?? 0UL;

                    string otherTokenSubject = otherTokenPolicy + otherTokenName;
                    string outRef = $"{txHash}{txIndex}";

                    return new[] { (otherTokenSubject, (outRef, adaReserve, otherTokenReserve)) };
                });
            })
            .GroupBy(x => x.otherTokenSubject)
            .ToDictionary(
                g => g.Key,
                g => g.First().Item2
            );
    }
}