using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Plutus.Address;

namespace Argus.Sync.Example.Models.Cardano.Jpeg.Datums;

[CborSerializable]
[CborConstr(0)]
public partial record Listing(
    [CborOrder(0)]
    CborIndefList<ListingPayout> Payouts,

    [CborOrder(1)]
    byte[] OwnerPkh
) : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record ListingPayout(
    [CborOrder(0)]
    Address Address,

    [CborOrder(1)]
    ulong Amount
) : CborBase;