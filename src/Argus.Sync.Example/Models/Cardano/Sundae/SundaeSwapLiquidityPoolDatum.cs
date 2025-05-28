using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.Sundae;

[CborSerializable]
[CborConstr(0)]
public partial record SundaeSwapLiquidityPoolDatum(
    [CborOrder(0)] byte[] Identifier,
    [CborOrder(1)] AssetClassTuple Assets,
    [CborOrder(2)] ulong CirculatingLp,
    [CborOrder(3)] ulong BidFeesPer10Thousand,
    [CborOrder(4)] ulong AskFeesPer10Thousand,
    [CborOrder(5)] Option<MultisigScript> FeeManager,
    [CborOrder(6)] ulong MarketOpen,
    [CborOrder(7)] ulong ProtocolFees
) : CborBase;