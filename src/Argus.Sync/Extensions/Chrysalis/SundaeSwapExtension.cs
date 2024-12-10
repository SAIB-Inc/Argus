using Chrysalis.Cardano.Sundae.Types.Common;
using Chrysalis.Cbor.Types.Primitives;


public static class SundaeSwapExtension
{
    public static List<CborBytes> Value(this AssetClass asset)
        => asset switch
        {
            AssetClassIndefinite indefAsset => indefAsset.Value,
            AssetClassDefinite defAsset => defAsset.Value,
            _ => throw new NotImplementedException()
        };

    public static List<AssetClass> Value(this AssetClassTuple asset)
        => asset switch
        {
            AssetClassTupleIndef indefTuple => indefTuple.Value,
            AssetClassTupleDef defTuple => defTuple.Value,
            _ => throw new NotImplementedException()
        };
}