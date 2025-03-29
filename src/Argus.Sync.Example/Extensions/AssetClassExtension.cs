using Argus.Sync.Example.Models.Cardano.Sundae;
using Argus.Sync.Example.Models.Cardano.Common;
using Chrysalis.Cbor.Extensions;

namespace Argus.Sync.Example.Extensions;

public static class AssetClassExtension
{
    public static List<AssetClass>? AssetClassTuple(this AssetClassTuple self) =>
        self switch 
        {
            AssetClassTupleDef tupleDef => tupleDef.Value.Value,
            AssetClassTupleIndef tupleIndef => tupleIndef.Value.Value,
            _ => null
        };

    public static AssetClass? AssetClass(this AssetClass self) =>
        self switch 
        {
            AssetClassDefinite tupleDef => tupleDef,
            AssetClassIndefinite tupleIndef => tupleIndef,
            _ => null
        };

    public static List<CborBytes>? AssetClassValue(this AssetClass self) =>
        self switch 
        {
            AssetClassDefinite tupleDef => tupleDef.Value.Value,
            AssetClassIndefinite tupleIndef => tupleIndef.Value.Value,
            _ => null
        };
}