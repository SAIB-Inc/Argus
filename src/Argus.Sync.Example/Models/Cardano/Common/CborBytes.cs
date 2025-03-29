using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.Common;

[CborSerializable]
public partial record CborBytes(byte[] Value) : CborBase;