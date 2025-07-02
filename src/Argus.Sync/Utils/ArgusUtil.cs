using System.Formats.Cbor;
using Argus.Sync.Data.Models.Enums;
using Chrysalis.Cbor.Types.Cardano.Core;
using Microsoft.Extensions.Configuration;
using NSec.Cryptography;

namespace Argus.Sync.Utils;

public static class ArgusUtil
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

    public static Block? DeserializeBlockWithEra(ReadOnlyMemory<byte> blockCbor)
    {
        try
        {
            CborReader reader = new(blockCbor, CborConformanceMode.Lax);
            
            // In Chrysalis 0.7.10, the structure is directly [era, block] without the tag wrapper
            reader.ReadStartArray();
            Era era = (Era)reader.ReadInt32();
            ReadOnlyMemory<byte> blockBytes = reader.ReadEncodedValue(true);

            return era switch
            {
                Era.Shelley or Era.Allegra or Era.Mary or Era.Alonzo => AlonzoCompatibleBlock.Read(blockBytes),
                Era.Babbage => BabbageBlock.Read(blockBytes),
                Era.Conway => ConwayBlock.Read(blockBytes),
                _ => throw new NotSupportedException($"Unsupported era: {era}")
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ArgusUtil] Error deserializing block: {ex.Message}");
            throw;
        }
    }
}
