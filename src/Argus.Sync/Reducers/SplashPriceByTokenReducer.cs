using Argus.Sync.Data;
using Argus.Sync.Data.Models.Splash;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Reducers;

public partial class SplashByPriceTokenReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<PriceByToken> where T : SplashPriceByTokenDbContext, ISplashPriceByTokenDbContext
{
    private readonly string _splashScriptHash = configuration["SplashScriptHash"]!;

    public async Task RollBackwardAsync(ulong slot)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();
        ulong rollbackSlot = slot;
        IQueryable<PriceByToken> rollbackEntries = dbContext.PriceByToken.AsNoTracking().Where(stp => stp.Slot >= rollbackSlot);
        dbContext.PriceByToken.RemoveRange(rollbackEntries);

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task RollForwardAsync(Chrysalis.Cardano.Models.Core.Block.Block block)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();
        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        foreach (TransactionBody tx in transactions)
        {
            IEnumerable<TransactionOutput> transactionOutputs = tx.Outputs();
            ulong index = 0;
            foreach (TransactionOutput output in transactionOutputs)
            {
                index++;
                try
                {
                    string? address = output.Address().Value.ToBech32();

                    if (string.IsNullOrEmpty(address) || !address!.StartsWith("addr")) continue;

                    string pkh = Convert.ToHexString(output.Address().GetPublicKeyHash()).ToLowerInvariant();

                    if (pkh != _splashScriptHash) continue;

                    SplashLiquidityPool? liquidityPool = CborSerializer.Deserialize<SplashLiquidityPool>(output?.DatumInfo()!.Value.Data!);

                    string tokenXPolicy = Convert.ToHexString(liquidityPool!.AssetX.PolicyId.Value).ToLowerInvariant();
                    string tokenXName = Convert.ToHexString(liquidityPool.AssetX.AssetName.Value).ToLowerInvariant();
                    string tokenYPolicy = Convert.ToHexString(liquidityPool.AssetY.PolicyId.Value).ToLowerInvariant();
                    string tokenYName = Convert.ToHexString(liquidityPool.AssetY.PolicyId.Value).ToLowerInvariant();

                    if (tokenXPolicy == string.Empty || tokenYPolicy == string.Empty)
                    {
                        ulong loveLaceReserve = output!.Amount().TransactionValueLovelace().Lovelace.Value;
                        decimal adaReserve = (decimal)loveLaceReserve / 1_000_000;

                        if (adaReserve < 10_000) continue;

                        string otherTokenPolicy = tokenXPolicy == string.Empty ? tokenYPolicy : tokenXPolicy;
                        string otherTokenName = tokenXName == string.Empty ? tokenYName : tokenXName;
                        string otherTokenHexName = tokenXName == string.Empty ? tokenYName : tokenXName;
                        string unit = otherTokenPolicy + otherTokenName;

                        ulong otherTokenReserve = output!.Amount().TransactionValueLovelace().MultiAsset.Value.ToDictionary(k => Convert.ToHexString(k.Key.Value), v => v.Value.Value.ToDictionary(
                            k => Convert.ToHexString(k.Key.Value),
                            v => v.Value.Value
                            ))[otherTokenPolicy][otherTokenName];


                        decimal price = adaReserve / otherTokenReserve * 1_000_000;

                        PriceByToken priceEntry = new()
                        {
                            Slot = block.Slot(),
                            TxHash = tx.Id(),
                            TxIndex = index,
                            PolicyId = otherTokenPolicy,
                            AssetName = otherTokenName,
                            Price = (ulong)price
                        };

                        dbContext.PriceByToken.Add(priceEntry);
                    }
                }
                catch
                {

                }

            }
        }

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }
}