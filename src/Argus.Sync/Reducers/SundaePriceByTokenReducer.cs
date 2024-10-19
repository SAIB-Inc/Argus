using System.Text.Json;
using Argus.Sync.Data;
using Argus.Sync.Data.Models.SundaeSwap;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Utils;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cbor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Block = Chrysalis.Cardano.Models.Core.Block.Block;

namespace Argus.Sync.Reducers;

public class SundaePriceByTokenReducer<T>(
    IDbContextFactory<T> dbContextFactory,
    IConfiguration configuration
) : IReducer<SundaeSwapTokenPrice> where T : CardanoDbContext
{
    private T _dbContext = default!;
    private readonly string _sundaeSwapScriptHash = configuration["SundaeSwapScriptHash"] ?? "e0302560ced2fdcbfcb2602697df970cd0d6a38f94b32703f51c312b";
    
    public async Task RollBackwardAsync(ulong slot)
    {
        _dbContext = dbContextFactory.CreateDbContext();
        _dbContext.SundaeSwapTokenPrices.RemoveRange(_dbContext.SundaeSwapTokenPrices.AsNoTracking().Where(b => b.Slot >= slot));
        
        await _dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        _dbContext = await dbContextFactory.CreateDbContextAsync();
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
                    
                    CborEncodedValue? encodedDatumValue = CborSerializer.Deserialize<CborEncodedValue>(transactionOutput?.GetDatumInfo()!.Value.Value!);
                    if (pkh != _sundaeSwapScriptHash || encodedDatumValue?.Value is null) continue;
                    
                    SundaeSwapLiquidityPool liquidityPool = CborSerializer.Deserialize<SundaeSwapLiquidityPool>(encodedDatumValue.Value)!;
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
                        
                        SundaeSwapTokenPrice sundaeSwapTokenPrice = new()
                        {
                            TokenXSubject = tokenXPolicy + tokenXName,
                            TokenYSubject = tokenYPolicy + tokenXName,
                            TokenXPrice = adaReserve,
                            TokenYPrice = otherTokenReserve,
                            Slot = block.Slot(),
                            TxHash = transaction.TransactionId(),
                            TxIndex = txIndex
                        };
                        
                        _dbContext.SundaeSwapTokenPrices.Add(sundaeSwapTokenPrice);
                    }
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }
        
        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
    }
}