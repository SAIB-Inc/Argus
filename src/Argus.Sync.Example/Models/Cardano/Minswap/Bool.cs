using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.Minswap;

[CborSerializable]
[CborUnion]
public abstract partial record Bool: CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record False: Bool;

[CborSerializable]
[CborConstr(1)]
public partial record True : Bool;