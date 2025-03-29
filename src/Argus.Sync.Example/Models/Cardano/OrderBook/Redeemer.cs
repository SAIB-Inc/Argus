using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.OrderBook;

[CborSerializable]
[CborConstr(0)]
public partial record AcceptRedeemer : CborBase;

[CborSerializable]
[CborConstr(1)]
public partial record CancelRedeemer : CborBase;