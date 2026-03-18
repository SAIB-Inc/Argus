using System.Formats.Cbor;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Microsoft.Extensions.Configuration;

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

    public static IBlock? DeserializeBlockWithEra(ReadOnlyMemory<byte> blockCbor)
    {
        CborReader reader = new(blockCbor, CborConformanceMode.Lax);

        // N2C format: Tag(24, ByteString([era, block]))
        // Read and verify tag 24
        var tag = reader.ReadTag();
        if (tag != CborTag.EncodedCborDataItem)
        {
            throw new InvalidOperationException($"Expected CBOR tag 24, got {tag}");
        }

        // Read the byte string containing [era, block]
        var innerBytes = reader.ReadByteString();

        var blockWithEra = CborSerializer.Deserialize<BlockWithEra>(innerBytes);
        return blockWithEra.Block;
    }
}
