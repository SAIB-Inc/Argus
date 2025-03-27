using Argus.Sync.Utils;
using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Argus.Sync.Extensions;

public static class BlockHeaderExtension
{
    public static string Hash(this BlockHeader self) => Convert.ToHexString(HashUtil.ToBlake2b256(self.Raw!.Value)).ToLowerInvariant();
}