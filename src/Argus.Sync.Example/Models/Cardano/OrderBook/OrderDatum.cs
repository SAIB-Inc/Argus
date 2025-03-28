

using Argus.Sync.Example.Models.Cardano.Sundae;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.OrderBook;

[CborSerializable]
[CborConstr(0)]
public record OrderDatum(
    [CborOrder(0)]
    byte[] Owner,

    [CborOrder(1)]
    AssetClass Asset,

    [CborOrder(2)]
    ulong Quantity
) : CborBase;