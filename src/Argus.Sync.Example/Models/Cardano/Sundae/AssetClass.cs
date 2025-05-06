using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.Sundae;

[CborSerializable]
[CborList]
public partial record AssetClass(
    [CborOrder(0)] byte[] PolicyId,
    [CborOrder(1)] byte[] AssetName
) : CborBase;


[CborSerializable]
[CborList]
public partial record AssetClassTuple(
    [CborOrder(0)] AssetClass AssetX,
    [CborOrder(1)] AssetClass AssetY
) : CborBase;