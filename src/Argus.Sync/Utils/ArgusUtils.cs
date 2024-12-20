using System.Formats.Cbor;
using Argus.Sync.Data.Models.Enums;
using Chrysalis.Cardano.Core.Types.Block;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Converters;
using Microsoft.Extensions.Configuration;
using NSec.Cryptography;
using Pallas.NET.Models;

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

    public static NetworkType GetNetworkType(IConfiguration configuration) => (NetworkType)configuration.GetValue("CardanoNetworkMagic", 2);

    public static byte[] ToBlake2b(this byte[] input)
    {
        Blake2b algorithm = HashAlgorithm.Blake2b_256;
        return algorithm.Hash(input);
    }

    public static byte[] GetPublicKeyHash(this Address address)
    {
        byte[] dst = new byte[28];
        Buffer.BlockCopy(address.Value, 1, dst, 0, dst.Length);
        return dst;
    }

    public static Block? DeserializeBlockWithEra(byte[] blockCbor)
    {
        CborReader reader = new(blockCbor);
        reader.ReadStartArray();
        Era era = (Era)reader.ReadInt32();
        byte[] blockBytes = reader.ReadEncodedValue().ToArray();

        return era switch
        {
            Era.Allegra or Era.Mary or Era.Alonzo => CborSerializer.Deserialize<AlonzoCompatibleBlock>(blockBytes),
            Era.Babbage => CborSerializer.Deserialize<BabbageBlock>(blockBytes),
            Era.Conway => CborSerializer.Deserialize<ConwayBlock>(blockBytes),
            _ => throw new NotSupportedException($"Unsupported era: {era}")
        };
    }
}
