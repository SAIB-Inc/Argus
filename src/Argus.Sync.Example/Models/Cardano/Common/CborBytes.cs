using Chrysalis.Cbor.Types;

namespace Argus.Sync.Example.Models.Cardano.Common;

public partial record CborBytes(byte[] Value) : CborBase;