using System.Text;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Internal;

namespace Argus.Sync.Example.Api;

public class SundaeSwapService(IDbContextFactory<TestDbContext> dbContextFactory)
{
    public async Task<object> FetchPricesAsync(int limit = 10, string? pair = null)
    {
        using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();
        var liquidityPoolsQuery = dbContext.SundaeSwapLiquidityPools.AsNoTracking();

        if (pair != null)
        {
            liquidityPoolsQuery = liquidityPoolsQuery.Where(p => p.Pair == pair);
        }

        var liquidityPools = await liquidityPoolsQuery
            .OrderByDescending(p => p.Slot)
            .Take(limit)
            .ToListAsync();

        var prices = liquidityPools
            .Select(lp =>
            {
                string assetX = string.IsNullOrEmpty(lp.AssetX) ? "ada" : lp.AssetX.Split('.').Last();
                string assetY = string.IsNullOrEmpty(lp.AssetY) ? "ada" : lp.AssetY.Split('.').Last();

                ulong? assetXReserve = assetX == "ada"
                    ? lp.TxOutput.Amount().Lovelace()
                    : lp.TxOutput.Amount().QuantityOf(lp.AssetX.Replace(".", "")
                );
                ulong? assetYReserve = assetY == "ada"
                    ? lp.TxOutput.Amount().Lovelace()
                    : lp.TxOutput.Amount().QuantityOf(lp.AssetY.Replace(".", "")
                );

                if (assetXReserve is null || assetXReserve == 0 || assetYReserve is null || assetYReserve == 0)
                {
                    return null;
                }

                decimal? priceByAssetX = (decimal)assetYReserve / assetXReserve;
                decimal? priceByAssetY = (decimal)assetXReserve / assetYReserve;

                return new
                {
                    lp.Slot,
                    lp.Pair,
                    PriceByAssetX = priceByAssetX,
                    PriceByAssetY = priceByAssetY
                };
            })
            .Where(p => p != null);

        return prices;
    }
}