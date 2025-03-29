using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Argus.Sync.Example.Models.Cardano.Sundae;

[CborSerializable]
[CborUnion]
public abstract partial record MultisigScript : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record Signature(byte[] KeyHash) : MultisigScript;

[CborSerializable]
[CborConstr(1)]
public record AllOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(2)]
public record AnyOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(3)]
public record AtLeast(ulong Required, CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(4)]
public record Before(PosixTime Time) : MultisigScript;

[CborSerializable]
[CborConstr(5)]
public record After(PosixTime Time) : MultisigScript;

[CborSerializable]
[CborConstr(6)]
public record Script(byte[] ScriptHash) : MultisigScript;