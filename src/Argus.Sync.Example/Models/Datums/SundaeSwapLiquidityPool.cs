using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Datums;

// Aiken type definition:
/// The current state of a AMM liquidity pool at a UTXO.
// pub type PoolDatum {
//   identifier: Ident,
//   assets: (AssetClass, AssetClass),
//   circulating_lp: Int,
//   bid_fees_per_10_thousand: Int,
//   ask_fees_per_10_thousand: Int,
//   fee_manager: Option<multisig.MultisigScript>,
//   market_open: Int,
//   protocol_fees: Int,
// }

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