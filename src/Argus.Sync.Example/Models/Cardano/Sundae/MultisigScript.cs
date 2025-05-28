using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Argus.Sync.Example.Models.Cardano.Sundae;

// Aiken type definition:
// pub type MultisigScript {
//   Signature { key_hash: ByteArray }
//   AllOf { scripts: List<MultisigScript> }
//   AnyOf { scripts: List<MultisigScript> }
//   AtLeast { required: Int, scripts: List<MultisigScript> }
//   Before { time: Int }
//   After { time: Int }
//   Script { script_hash: ByteArray }
// }

[CborSerializable]
[CborUnion]
public abstract partial record MultisigScript : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record Signature([CborOrder(0)] byte[] KeyHash) : MultisigScript;

[CborSerializable]
[CborConstr(1)]
public partial record AllOf([CborOrder(0)] CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(2)]
public partial record AnyOf([CborOrder(0)] CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(3)]
public partial record AtLeast([CborOrder(0)] ulong Required, [CborOrder(1)] CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(4)]
public partial record Before([CborOrder(0)] PosixTime Time) : MultisigScript;

[CborSerializable]
[CborConstr(5)]
public partial record After([CborOrder(0)] PosixTime Time) : MultisigScript;

[CborSerializable]
[CborConstr(6)]
public partial record Script([CborOrder(0)] byte[] ScriptHash) : MultisigScript;