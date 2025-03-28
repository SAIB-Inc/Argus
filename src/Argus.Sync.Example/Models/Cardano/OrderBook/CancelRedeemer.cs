using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.OrderBook;

[CborSerializable]
[CborConstr(1)]
public record CancelRedeemer() : CborBase;