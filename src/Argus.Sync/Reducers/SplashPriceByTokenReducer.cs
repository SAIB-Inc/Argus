using Argus.Sync.Data;
using Argus.Sync.Data.Models.Splash;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Reducers;

public partial class SplashByPriceTokenReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<SplashTokenPrice> where T : CardanoDbContext
{
    private readonly string _splashScriptHash = configuration["SplashScriptHash"]!;

    public async Task RollBackwardAsync(ulong slot)
    {
        using CardanoDbContext _dbContext = dbContextFactory.CreateDbContext();

        ulong rollbackSlot = slot;
        IQueryable<SplashTokenPrice> rollbackEntries = _dbContext.SplashTokenPrice.AsNoTracking().Where(stp => stp.Slot > rollbackSlot);
        _dbContext.SplashTokenPrice.RemoveRange(rollbackEntries);

        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
    }

    public async Task RollForwardAsync(Chrysalis.Cardano.Models.Core.Block.Block block)
    {
        using CardanoDbContext _dbContext = dbContextFactory.CreateDbContext();
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

                    SplashLiquidityPool? liquidityPool = CborSerializer.Deserialize<SplashLiquidityPool>(output?.GetDatumInfo()!.Value.Data!);
                 
                    string tokenXPolicy = Convert.ToHexString(liquidityPool!.AssetX.PolicyId.Value).ToLowerInvariant();
                    string tokenXName = Convert.ToHexString(liquidityPool.AssetX.AssetName.Value).ToLowerInvariant();
                    string tokenYPolicy = Convert.ToHexString(liquidityPool.AssetY.PolicyId.Value).ToLowerInvariant();
                    string tokenYName = Convert.ToHexString(liquidityPool.AssetY.PolicyId.Value).ToLowerInvariant();

                    if (tokenXPolicy == string.Empty || tokenYPolicy == string.Empty)
                    {
                        ulong loveLaceReserve = output.Amount().TransactionValueLovelace().Lovelace.Value;
                        decimal adaReserve = (decimal)loveLaceReserve / 1_000_000;

                        if (adaReserve < 10_000) continue;

                        string otherTokenPolicy = tokenXPolicy == string.Empty ? tokenYPolicy : tokenXPolicy;
                        string otherTokenName = tokenXName == string.Empty ? tokenYName : tokenXName;
                        string otherTokenHexName = tokenXName == string.Empty ? tokenYName : tokenXName;
                        string unit = otherTokenPolicy + otherTokenName;

                        ulong otherTokenReserve = output.Amount().TransactionValueLovelace().MultiAsset.Value.ToDictionary(k => Convert.ToHexString(k.Key.Value), v => v.Value.Value.ToDictionary(
                            k => Convert.ToHexString(k.Key.Value),
                            v => v.Value.Value
                            ))[otherTokenPolicy][otherTokenName];


                        decimal price = adaReserve / otherTokenReserve * 1_000_000 ; 

                      
                        SplashTokenPrice priceEntry = new() { Slot = block.Slot(), TxHash = Convert.ToHexString(CborSerializer.Serialize(tx).ToBlake2b()), TxIndex = index, PolicyId = otherTokenPolicy, AssetName = otherTokenName, Price = (ulong)price};

                        _dbContext.SplashTokenPrice.Add(priceEntry);
                    }
                }
                catch (Exception e)
                {
                    
                }

            }
        }

        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
    }
}