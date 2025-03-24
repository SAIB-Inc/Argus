using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Data.Cardano;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record CancelRedeemer() : CborBase;