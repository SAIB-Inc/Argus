using NSec.Cryptography;

namespace Argus.Sync.Utils;

public static class HashUtil
{
    public static byte[] ToBlake2b256(byte[] input)
    {
        Blake2b algorithm = HashAlgorithm.Blake2b_256;
        return algorithm.Hash(input);
    }

    public static byte[] ToBlake2b256(ReadOnlyMemory<byte> input)
    {
        return ToBlake2b256(input.ToArray());
    }
}