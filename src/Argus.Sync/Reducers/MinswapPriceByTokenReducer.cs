using Argus.Sync.Data;
using Argus.Sync.Data.Models.Minswap;
using Argus.Sync.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Chrysalis.Cardano.Core.Types.Block;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Extensions;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cardano.Minswap.Types.Datums;
using Chrysalis.Cbor.Converters;

namespace Argus.Sync.Reducers;

public class MinswapPriceByTokenReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<PriceByToken> where T : MinswapPriceByTokenDbContext, IMinswapPriceByTokenDbContext

{
    private readonly string _minsSwapScriptHash = configuration["MinswapScriptHash"] ?? "";

    public async Task RollBackwardAsync(ulong slot)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();
        dbContext.PriceByToken.RemoveRange(dbContext.PriceByToken.AsNoTracking().Where(b => b.Slot >= slot));
        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();
        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        foreach (TransactionBody transaction in transactions)
        {
            ulong txIndex = 0;
            foreach (TransactionOutput transactionOutput in transaction.Outputs())
            {

                string? outputBech32Addr = transactionOutput.Address()?.ToBech32();

                if (string.IsNullOrEmpty(outputBech32Addr) || !outputBech32Addr.StartsWith("addr")) continue;

                string pkh = Convert.ToHexString(transactionOutput.Address()!.GetPublicKeyHash()).ToLowerInvariant();

                if (pkh != _minsSwapScriptHash)
                {
                    txIndex++;
                    continue;
                }

                MinswapLiquidityPool? liquidityPool = CborSerializer.Deserialize<MinswapLiquidityPool>(transactionOutput?.Datum()!);

                string tokenXPolicy = Convert.ToHexString(liquidityPool!.AssetX.PolicyId.Value).ToLowerInvariant();
                string tokenXName = Convert.ToHexString(liquidityPool.AssetX.AssetName.Value).ToLowerInvariant();
                string tokenYPolicy = Convert.ToHexString(liquidityPool.AssetY.PolicyId.Value).ToLowerInvariant();
                string tokenYName = Convert.ToHexString(liquidityPool.AssetY.AssetName.Value).ToLowerInvariant();

                if (tokenXPolicy == string.Empty || tokenYPolicy == string.Empty)
                {
                    ulong adaReserve = transactionOutput?.Amount()?.Lovelace() ?? 0UL;

                    // if reserve is less than 10k ada, skip 
                    if (adaReserve < 10_000) continue;

                    string otherTokenPolicy = tokenXPolicy == string.Empty ? tokenYPolicy : tokenXPolicy;
                    string otherTokenName = tokenXName == string.Empty ? tokenYName : tokenXName;

                    // calculate the price 
                    ulong otherTokenReserve = transactionOutput?.Amount()?
                        .MultiAsset()?.ToDictionary(k => k.Key,
                            v =>
                                v.Value.Value.ToDictionary(
                                    k => Convert.ToHexString(k.Key.Value).ToLowerInvariant(),
                                    v => v.Value.Value
                                ))[otherTokenPolicy][otherTokenName] ?? 0UL;

                    PriceByToken minSwapTokenPrice = new(
                        block.Slot() ?? 0UL,
                        transaction.Id(),
                        txIndex,
                        $"{tokenXPolicy}{tokenXName}",
                        $"{tokenYPolicy}{tokenYName}",
                        adaReserve,
                        otherTokenReserve
                    );

                    dbContext.PriceByToken.Add(minSwapTokenPrice);
                }
                txIndex++;
            }
        }

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task<ulong?> QueryTip()
    {
        using T dbContext = await dbContextFactory.CreateDbContextAsync();
        ulong maxSlot = await dbContext.PriceByToken.MaxAsync(x => (ulong?)x.Slot) ?? 0;
        return maxSlot;
    }
}
