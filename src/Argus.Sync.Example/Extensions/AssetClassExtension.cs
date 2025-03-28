using Argus.Sync.Example.Models.Cardano.Common;
using Argus.Sync.Example.Models.Cardano.Sundae;
using Argus.Sync.Example.Models.Enums;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

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