using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;

namespace Argus.Sync.Example.Api;

public class SundaeSwapService(IDbContextFactory<TestDbContext> dbContextFactory)
{
    public async Task<object> FetchPricesAsync(int limit)
    {
        using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();
        List<Models.SundaeSwapLiquidityPool> liquidityPools = await dbContext.SundaeSwapLiquidityPools
            .AsNoTracking()
            .OrderByDescending(p => p.Slot)
            .Take(limit)
            .ToListAsync();

        var prices = liquidityPools
            .Select(lp =>
            {
                string assetX = string.IsNullOrEmpty(lp.AssetX) ? "ADA" : lp.AssetX.Split('.').Last();
                string assetY = string.IsNullOrEmpty(lp.AssetY) ? "ADA" : lp.AssetY.Split('.').Last();

                ulong? assetXReserve = assetX == "ADA"
                    ? lp.TxOutput.Amount().Lovelace()
                    : lp.TxOutput.Amount().QuantityOf(lp.AssetX.Replace(".", "")
                );
                ulong? assetYReserve = assetY == "ADA"
                    ? lp.TxOutput.Amount().Lovelace()
                    : lp.TxOutput.Amount().QuantityOf(lp.AssetY.Replace(".", "")
                );
                decimal? priceByAssetX = (decimal)assetYReserve! / assetXReserve;
                decimal? priceByAssetY = (decimal)assetXReserve! / assetYReserve;

                return new
                {
                    lp.Slot,
                    AssetX = assetX,
                    AssetY = assetY,
                    PriceByAssetX = priceByAssetX,
                    PriceByAssetY = priceByAssetY
                };
            });

        return prices;
    }
}