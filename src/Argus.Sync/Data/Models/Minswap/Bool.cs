using Chrysalis.Cbor;
namespace Argus.Sync.Data.Models.Minswap;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(True),
    typeof(False),
])]
public record Bool: RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record False: Bool;

[CborSerializable(CborType.Constr, Index = 1)]
public record True : Bool;