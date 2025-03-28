using Argus.Sync.Example.Models.Cardano.Common;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.Sundae;


[CborSerializable]
[CborUnion]
public abstract partial record AssetClass : CborBase;

[CborSerializable]
[CborList]
public partial record AssetClassIndefinite([CborOrder(0)] CborIndefList<CborBytes> Value) : AssetClass;

[CborSerializable]
[CborList]
public partial record AssetClassDefinite([CborOrder(0)] CborDefList<CborBytes> Value) : AssetClass;


[CborSerializable]
[CborUnion]
public abstract partial record AssetClassTuple : CborBase;

[CborSerializable]
[CborList]
public partial record AssetClassTupleIndef([CborOrder(0)] CborIndefList<AssetClass> Value) : AssetClassTuple;

[CborSerializable]
[CborList]
public partial record AssetClassTupleDef([CborOrder(0)] CborDefList<AssetClass> Value) : AssetClassTuple;