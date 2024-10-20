using Argus.Sync.Data;
using Argus.Sync.Data.Models.SundaeSwap;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Block = Chrysalis.Cardano.Models.Core.Block.Block;

namespace Argus.Sync.Reducers;

public class SundaePriceByTokenReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<PriceByToken> where T : SundaePriceByTokenDbContext, ISundaePriceByTokenDbContext
{
    private readonly string _sundaeSwapScriptHash = configuration["SundaeSwapScriptHash"] ?? "e0302560ced2fdcbfcb2602697df970cd0d6a38f94b32703f51c312b";

    public async Task RollBackwardAsync(ulong slot)
    {
        await using T dbContext = await dbContextFactory.CreateDbContextAsync();
        dbContext.PriceByToken.RemoveRange(dbContext.PriceByToken.AsNoTracking().Where(b => b.Slot >= slot));
        await dbContext.SaveChangesAsync();
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
                try
                {
                    string? outputBech32Addr = transactionOutput.Address().Value.ToBech32();

                    if (string.IsNullOrEmpty(outputBech32Addr) || !outputBech32Addr.StartsWith("addr")) continue;

                    string pkh = Convert.ToHexString(transactionOutput.Address().GetPublicKeyHash()).ToLowerInvariant();


                    if (pkh != _sundaeSwapScriptHash) continue;

                    SundaeSwapLiquidityPool liquidityPool = CborSerializer.Deserialize<SundaeSwapLiquidityPool>(transactionOutput?.DatumInfo()!.Value.Data!)!;
                    AssetClassTuple assets = liquidityPool.Assets!;

                    string tokenXPolicy = Convert.ToHexString(assets.Value[0].Value[0].Value).ToLowerInvariant();
                    string tokenXName = Convert.ToHexString(assets.Value[0].Value[1].Value).ToLowerInvariant();
                    string tokenYPolicy = Convert.ToHexString(assets.Value[1].Value[0].Value).ToLowerInvariant();
                    string tokenYName = Convert.ToHexString(assets.Value[1].Value[1].Value).ToLowerInvariant();

                    if (tokenXPolicy == string.Empty || tokenYPolicy == string.Empty)
                    {
                        ulong adaReserve = transactionOutput!.Amount().TransactionValueLovelace().Lovelace.Value;

                        // if reserve is less than 10k ada, skip
                        if (adaReserve < 10_000) continue;

                        string otherTokenPolicy = tokenXPolicy == string.Empty ? tokenYPolicy : tokenXPolicy;
                        string otherTokenName = tokenXName == string.Empty ? tokenYName : tokenXName;

                        // calculate the price
                        ulong otherTokenReserve = transactionOutput!.Amount().TransactionValueLovelace()
                            .MultiAsset.Value.ToDictionary(k => Convert.ToHexString(k.Key.Value).ToLowerInvariant(),
                                v =>
                                    v.Value.Value.ToDictionary(
                                        k => Convert.ToHexString(k.Key.Value).ToLowerInvariant(),
                                        v => v.Value.Value
                                    ))[otherTokenPolicy][otherTokenName];

                        PriceByToken sundaeSwapTokenPrice = new(
                            block.Slot(),
                            transaction.Id(),
                            txIndex,
                            $"{tokenXPolicy}{tokenXName}",
                            $"{tokenYPolicy}{tokenYName}",
                            adaReserve,
                            otherTokenReserve
                        );

                        dbContext.PriceByToken.Add(sundaeSwapTokenPrice);
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }

        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }
}