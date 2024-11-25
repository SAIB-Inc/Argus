using Argus.Sync.Data.Models.SundaeSwap;
using Chrysalis.Cardano.Cbor;


public static class SundaeSwapExtension
{
    public static CborBytes[] Value(this AssetClass asset)
        => asset switch
        {
            AssetClassIndefinite indefAsset => indefAsset.Value,
            AssetClassDefinite defAsset => defAsset.Value,
            _ => throw new NotImplementedException()
        };

    public static AssetClass[] Value(this AssetClassTuple asset)
        => asset switch
        {
            AssetClassTupleIndef indefTuple => indefTuple.Value,
            AssetClassTupleDef defTuple => defTuple.Value,
            _ => throw new NotImplementedException()
        };
}