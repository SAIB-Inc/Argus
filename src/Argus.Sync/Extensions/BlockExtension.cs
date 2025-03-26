using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using NSec.Cryptography;

namespace Argus.Sync.Extensions;

public static class BlockExtension
{

    public static BlockHeaderBody Header(this Block self)
    {
        return self switch
        {
            AlonzoCompatibleBlock a => a.Header.HeaderBody,
            BabbageBlock b => b.Header.HeaderBody,
            ConwayBlock c => c.Header.HeaderBody,
            _ => throw new NotImplementedException()
        };
    }

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

    public static string Hash(this BlockHeaderBody self)
    {
        return self switch
        {
            AlonzoHeaderBody a => Convert.ToHexString(ToBlake2b256(a.Raw!.Value)),
            BabbageHeaderBody b => Convert.ToHexString(ToBlake2b256(b.Raw!.Value)),
            _ => throw new NotImplementedException()
        };
    }

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

