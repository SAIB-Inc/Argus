using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using NSec.Cryptography;

namespace Argus.Sync.Extensions;

public static class BlockExtension
{

    public static BlockHeaderBody HeaderBody(this Block self)
    {
        return self switch
        {
            AlonzoCompatibleBlock a => a.Header.HeaderBody,
            BabbageBlock b => b.Header.HeaderBody,
            ConwayBlock c => c.Header.HeaderBody,
            _ => throw new NotImplementedException()
        };
    }

    public static BlockHeader Header(this Block self) => self switch
    {
        AlonzoCompatibleBlock a => a.Header,
        BabbageBlock b => b.Header,
        ConwayBlock c => c.Header,
        _ => throw new NotImplementedException()
    };

    public static ulong Slot(this BlockHeaderBody self)
    {
        return self switch
        {
            AlonzoHeaderBody a => a.Slot,
            BabbageHeaderBody b => b.Slot,
            _ => throw new NotImplementedException()
        };
    }

    public static ulong Number(this BlockHeaderBody self)
    {
        return self switch
        {
            AlonzoHeaderBody a => a.BlockNumber,
            BabbageHeaderBody b => b.BlockNumber,
            _ => throw new NotImplementedException()
        };
    }

    public static string Hash(this BlockHeader self) => Convert.ToHexString(ToBlake2b256(self.Raw!.Value)).ToLowerInvariant();

    public static byte[] ToBlake2b256(this byte[] input)
    {
        Blake2b algorithm = HashAlgorithm.Blake2b_256;
        return algorithm.Hash(input);
    }

    public static byte[] ToBlake2b256(this ReadOnlyMemory<byte> input)
    {
        return ToBlake2b256(input.ToArray());
    }
}

