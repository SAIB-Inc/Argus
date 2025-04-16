using Argus.Sync.Example.Models;
using Argus.Sync.Example.Models.Enums;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using Block = Chrysalis.Cbor.Types.Cardano.Core.Block;
using Argus.Sync.Reducers;
using Argus.Sync.Example.Extensions;
using Argus.Sync.Example.Models.Cardano.Sundae;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Argus.Sync.Example.Models.Cardano.Common;

namespace Argus.Sync.Example.Reducers.PriceReducers;

public class SundaePriceByTokenReducer(
    IDbContextFactory<TestDbContext> dbContextFactory,
    IConfiguration configuration
) : TokenPriceBaseReducer(configuration)
{
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
                OutRef: tp.Value.outRef.ToLowerInvariant(),
                Slot: slot,
                TokenXSubject: string.Empty,
                TokenYSubject: subject,
                TokenXPrice: tp.Value.adaReserve,
                TokenYPrice: tp.Value.otherTokenReserve,
                PlatformType: TokenPricePlatformType.SundaeSwap
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
                        outputAddressPkh != SundaeSwapScriptHash) return [];

                    if (!TryDeserializeLiquidityPool(o.Datum(), out SundaeSwapLiquidityPool liquidityPool)) return [];

                    AssetClassTuple assets = liquidityPool.Assets;

                    (string tokenXPolicy, string tokenXName) =
                        (Convert.ToHexStringLower(assets.AssetX.PolicyId), Convert.ToHexStringLower(assets.AssetX.AssetName));

                    (string tokenYPolicy, string tokenYName) =
                        (Convert.ToHexStringLower(assets.AssetY.PolicyId), Convert.ToHexStringLower(assets.AssetY.AssetName));

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

public record TokenIdentifier(string PolicyId, string AssetName);