using CardanoSharp.Wallet.Enums;
using CardanoSharpAddress = CardanoSharp.Wallet.Models.Addresses.Address;
using Microsoft.Extensions.Configuration;

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
}