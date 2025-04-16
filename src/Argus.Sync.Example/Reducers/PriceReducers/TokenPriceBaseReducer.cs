using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Argus.Sync.Example.Reducers.PriceReducers;

public abstract class TokenPriceBaseReducer(IConfiguration configuration)
{
    protected string SundaeSwapScriptHash => configuration.GetValue("SundaeSwapScriptHash", "e0302560ced2fdcbfcb2602697df970cd0d6a38f94b32703f51c312b");
    protected string MinswapScriptHash => configuration.GetValue("MinswapScriptHash", "ea07b733d932129c378af627436e7cbc2ef0bf96e0036bb51b3bde6b");
    protected string SplashScriptHash => configuration.GetValue("SplashScriptHash", "9dee0659686c3ab807895c929e3284c11222affd710b09be690f924d");
    protected string JpegV1ScriptHash => configuration.GetValue("JpegV1ScriptHash", "c727443d77df6cff95dca383994f4c3024d03ff56b02ecc22b0f3f65");

    protected static bool TryExtractAddressHash(TransactionOutput output, out string addressHash)
    {
        addressHash = string.Empty;
        try
        {
            WalletAddress address = new(output.Address());
            addressHash = Convert.ToHexStringLower(address.GetPaymentKeyHash()!);
            return !string.IsNullOrEmpty(addressHash);
        }
        catch
        {
            return false;
        }
    }

    protected static bool TryDeserializeLiquidityPool<T>(byte[]? datum, out T liquidityPool)  where T : CborBase
    {
        liquidityPool = default!;
        if (datum is null) return false;
        try
        {
            liquidityPool = CborSerializer.Deserialize<T>(datum);
            return liquidityPool is not null;
        }
        catch(Exception e)
        {
            // Log the error if necessary
            Console.WriteLine($"Error deserializing liquidity pool: {e.Message}");
            return false;
        }
    }
}