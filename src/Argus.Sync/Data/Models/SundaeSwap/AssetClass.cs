using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;
using CborBytes = Chrysalis.Cardano.Cbor.CborBytes;

namespace Argus.Sync.Data.Models.SundaeSwap;
// [_
//      [_ h'', h''],
//      [_
//          h'f66d78b4a3cb3d37afa0ec36461e51ecbde00f26c8f0a68f94b69880',
//          h'69555344',
//      ],
// ] This should be a tuple but Chrysalis does not support tuple data types
[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(AssetClassIndefinite),
    typeof(AssetClassDefinite),
])]
public interface AssetClass : ICbor;

public record AssetClassIndefinite(CborBytes[] Value) : CborIndefiniteList<CborBytes>(Value), AssetClass;

public record AssetClassDefinite(CborBytes[] Value) : CborDefiniteList<CborBytes>(Value), AssetClass;


[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(AssetClassTupleIndef),
    typeof(AssetClassTupleDef),
])]
public interface AssetClassTuple : ICbor;

public record AssetClassTupleIndef(AssetClass[] Value) : CborIndefiniteList<AssetClass>(Value), AssetClassTuple;

public record AssetClassTupleDef(AssetClass[] Value) : CborDefiniteList<AssetClass>(Value), AssetClassTuple;