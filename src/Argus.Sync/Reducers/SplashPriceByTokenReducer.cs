using Argus.Sync.Data;
using Argus.Sync.Utils;
using Argus.Sync.Data.Models.Splash;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Chrysalis.Cardano.Core;
using Chrysalis.Utils;

namespace Argus.Sync.Reducers;

public partial class SplashPriceByTokenReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<PriceByToken> where T : SplashPriceByTokenDbContext, ISplashPriceByTokenDbContext
{
    private readonly string _splashScriptHash = configuration["SplashScriptHash"]!;

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

        foreach (TransactionBody tx in transactions)
        {
            IEnumerable<TransactionOutput> transactionOutputs = tx.Outputs();
            ulong txIndex = 0;
            foreach (TransactionOutput output in transactionOutputs)
            {

                string? address = output?.Address()?.Value.ToBech32();

                if (string.IsNullOrEmpty(address) || !address!.StartsWith("addr")) continue;

                string pkh = Convert.ToHexString(output!.Address()!.GetPublicKeyHash()).ToLowerInvariant();

                if (pkh != _splashScriptHash)
                {
                    txIndex++;
                    continue;
                }

                string txHash = tx.Id();

                SplashLiquidityPool? liquidityPool = CborSerializer.Deserialize<SplashLiquidityPool>(output?.DatumInfo()!);
                string tokenXPolicy = Convert.ToHexString(liquidityPool!.AssetX.PolicyId.Value).ToLowerInvariant();
                string tokenXName = Convert.ToHexString(liquidityPool.AssetX.AssetName.Value).ToLowerInvariant();
                string tokenYPolicy = Convert.ToHexString(liquidityPool.AssetY.PolicyId.Value).ToLowerInvariant();
                string tokenYName = Convert.ToHexString(liquidityPool.AssetY.AssetName.Value).ToLowerInvariant();

                if (tokenXPolicy == string.Empty || tokenYPolicy == string.Empty)
                {
                    ulong loveLaceReserve = output!.Amount()!.TransactionValueLovelace().Lovelace.Value;
                    decimal adaReserve = (decimal)loveLaceReserve / 1_000_000;

                    if (adaReserve < 10_000) continue;

                    string otherTokenPolicy = tokenXPolicy == string.Empty ? tokenYPolicy : tokenXPolicy;
                    string otherTokenName = tokenXName == string.Empty ? tokenYName : tokenXName;

                    ulong otherTokenReserve = output!.Amount()!.TransactionValueLovelace()
                        .MultiAsset.Value.ToDictionary(k => Convert.ToHexString(k.Key.Value).ToLowerInvariant(),
                            v =>
                                v.Value.Value.ToDictionary(
                                    k => Convert.ToHexString(k.Key.Value).ToLowerInvariant(),
                                    v => v.Value.Value
                                ))[otherTokenPolicy][otherTokenName];

                    PriceByToken splashTokenPrice = new(
                        block.Slot(),
                        tx.Id(),
                        txIndex,
                        $"{tokenXPolicy}{tokenXName}",
                        $"{tokenYPolicy}{tokenYName}",
                        loveLaceReserve,
                        otherTokenReserve
                    );

                    dbContext.PriceByToken.Add(splashTokenPrice);
                    txIndex++;
                }
                txIndex++;
            }
        }

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }
}