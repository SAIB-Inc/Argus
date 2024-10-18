using CardanoSharp.Wallet.Enums;
using Chrysalis.Cardano.Models.Core;
using CardanoSharpAddress = CardanoSharp.Wallet.Models.Addresses.Address;
using Microsoft.Extensions.Configuration;
using NSec.Cryptography; 

namespace Argus.Sync.Utils;

public static class ArgusUtils
{
    public static string GetTypeNameWithoutGenerics(Type type)
    {
        string typeName = type.Name;
        int genericCharIndex = typeName.IndexOf('`');
        if (genericCharIndex != -1)
        {
            typeName = typeName[..genericCharIndex];
        }
        return typeName;
    }
    
    public static NetworkType GetNetworkType(IConfiguration configuration)
    {
        return configuration.GetValue<int>("CardanoNetworkMagic") switch
        {
            764824073 => NetworkType.Mainnet,
            1 => NetworkType.Preprod,
            2 => NetworkType.Preview,
            _ => throw new NotImplementedException()
        };
    }
    
    public static string? ToBech32(this byte[] address)
    {
        try
        {
            return new CardanoSharpAddress(address).ToString();
        }
        catch
        {
            return null;
        }
    }

    public static byte[] ToBlake2b(byte[] input)
    {
        Blake2b algorithm = HashAlgorithm.Blake2b_256;
        return algorithm.Hash(input);
    }
    
    public static byte[] GetPublicKeyHash(this Address address)
    {
        byte[] dst = new byte[28];
        Buffer.BlockCopy((Array) address.Value, 1, (Array) dst, 0, dst.Length);
        return dst;
    }
}
