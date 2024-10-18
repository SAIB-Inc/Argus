using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Argus.Sync.Data.Models.SundaeSwap;
// [_
//      [_ h'', h''],
//      [_
//          h'f66d78b4a3cb3d37afa0ec36461e51ecbde00f26c8f0a68f94b69880',
//          h'69555344',
//      ],
// ] This should be a tuple but Chrysalis does not support tuple data types
[CborSerializable(CborType.Constr, Index = 0)]
public record AssetClass(CborBytes[] Value) : CborIndefiniteList<CborBytes>(Value);

public record AssetClassTuple(AssetClass[] Value) : CborIndefiniteList<AssetClass>(Value);