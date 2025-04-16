using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.Common;

// TODO: this should be a union type of AssetClasses

[CborSerializable]
[CborList]
public partial record AssetClass(
    [CborOrder(0)]
    byte[] PolicyId,

    [CborOrder(1)]
    byte[] AssetName
) : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record AssetClassConstr(
    [CborOrder(0)]
    byte[] PolicyId,

    [CborOrder(1)]
    byte[] AssetName
) : CborBase;

[CborSerializable]
[CborList]
public partial record AssetClassTuple(
    [CborOrder(0)]
    AssetClass AssetX,

    [CborOrder(1)]
    AssetClass AssetY
) : CborBase;