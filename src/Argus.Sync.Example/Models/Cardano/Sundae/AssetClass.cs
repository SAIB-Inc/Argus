using Argus.Sync.Example.Models.Cardano.Common;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.Sundae;


[CborSerializable]
[CborUnion]
public abstract partial record AssetClass : CborBase;

[CborSerializable]
public partial record AssetClassIndefinite(CborIndefList<CborBytes> Value) : AssetClass;

[CborSerializable]
public partial record AssetClassDefinite(CborDefList<CborBytes> Value) : AssetClass;

[CborSerializable]
[CborUnion]
public abstract partial record AssetClassTuple : CborBase;

[CborSerializable]
public partial record AssetClassTupleIndef(CborDefList<AssetClass> Value) : AssetClassTuple;

[CborSerializable]
public partial record AssetClassTupleDef(CborIndefList<AssetClass> Value) : AssetClassTuple;