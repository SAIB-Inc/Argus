

using Argus.Sync.Example.Models;
using Argus.Sync.Example.Models.Datums;
using Argus.Sync.Extensions;
using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Argus.Sync.Example.Reducers;

public class SundaeSwapReducer(
    IDbContextFactory<TestDbContext> dbContextFactory
) : IReducer<SundaeSwapLiquidityPool>
{
    private readonly string _scriptHash = "e0302560ced2fdcbfcb2602697df970cd0d6a38f94b32703f51c312b";

    public async Task RollBackwardAsync(ulong slot)
    {
        using TestDbContext dbContext = dbContextFactory.CreateDbContext();
        IQueryable<SundaeSwapLiquidityPool> dataToRollback = dbContext.SundaeSwapLiquidityPools.Where(p => p.Slot >= slot);
        dbContext.RemoveRange(dataToRollback);
        await dbContext.SaveChangesAsync();
    }

    public async Task RollForwardAsync(Block block)
    {
        ulong slot = block.Header().HeaderBody().Slot();
        using TestDbContext dbContext = dbContextFactory.CreateDbContext();

        IEnumerable<TransactionBody> transactions = block.TransactionBodies();

        foreach (TransactionBody transaction in transactions)
        {
            string txHash = transaction.Hash();
            List<TransactionOutput> outputs = [.. transaction.Outputs()];
            foreach (TransactionOutput? output in outputs)
            {
                if (TryDeserializeDatum(in output, out SundaeSwapLiquidityPoolDatum datum))
                {
                    int outputIndex = outputs.IndexOf(output);
                    string outRef = txHash + "#" + outputIndex;
                    string identifier = Convert.ToHexString(datum.Identifier).ToLowerInvariant();
                    string assetXPolicyId = Convert.ToHexString(datum.Assets.AssetX.PolicyId).ToLowerInvariant();
                    string assetXAssetName = Convert.ToHexString(datum.Assets.AssetX.AssetName).ToLowerInvariant();
                    string assetYPolicyId = Convert.ToHexString(datum.Assets.AssetY.PolicyId).ToLowerInvariant();
                    string assetYAssetName = Convert.ToHexString(datum.Assets.AssetY.AssetName).ToLowerInvariant();
                    string lpTokenPolicyId = Convert.ToHexString(datum.Identifier).ToLowerInvariant();
                    string lpTokenAssetName = Convert.ToHexString(datum.Identifier).ToLowerInvariant();
                    string assetX = assetXPolicyId + "." + assetXAssetName;
                    string assetY = assetYPolicyId + "." + assetYAssetName;
                    assetX = assetX == "." ? "" : assetX;
                    assetY = assetY == "." ? "" : assetY;
                    string lpToken = lpTokenPolicyId + "." + lpTokenAssetName;
                    ulong circulatingLp = datum.CirculatingLp;


                    SundaeSwapLiquidityPool liquidityPool = new(
                        slot,
                        outRef,
                        identifier,
                        assetX,
                        assetY,
                        lpToken,
                        circulatingLp,
                        output.Raw?.ToArray()!
                    );

                    dbContext.SundaeSwapLiquidityPools.Add(liquidityPool);
                }
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private bool TryDeserializeDatum(in TransactionOutput txOut, out SundaeSwapLiquidityPoolDatum datum)
    {
        datum = default!;

        try
        {
            WalletAddress addr = new(txOut.Address());
            byte[] keyHashBytes = addr.GetPaymentKeyHash() ?? [];
            string pkh = Convert.ToHexString(keyHashBytes).ToLowerInvariant();

            if (pkh != _scriptHash)
                return false;

            DatumOption? datumOption = txOut.DatumOption();
            if (datumOption == null)
                return false;

            var inlineDatum = new CborEncodedValue(datumOption.Data());
            datum = SundaeSwapLiquidityPoolDatum.Read(inlineDatum.GetValue());
            return datum is not null;
        }
        catch
        {
            return false;
        }
    }
}