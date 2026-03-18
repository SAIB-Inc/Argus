using System.Formats.Cbor;
using Argus.Sync.Data.Models.Enums;
using Chrysalis.Cbor.Types.Cardano.Core;

namespace Argus.Sync.Utils;

/// <summary>
/// Utility methods for Argus blockchain data processing.
/// </summary>
public static class ArgusUtil
{
    /// <summary>
    /// Gets the type name without generic arity suffix (e.g., backtick and number).
    /// </summary>
    /// <param name="type">The type to get the name for.</param>
    /// <returns>The type name without generic indicators.</returns>
    public static string GetTypeNameWithoutGenerics(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        string typeName = type.Name;
        int genericCharIndex = typeName.IndexOf('`', StringComparison.Ordinal);
        if (genericCharIndex != -1)
        {
            typeName = typeName[..genericCharIndex];
        }
        return typeName;
    }

    /// <summary>
    /// Deserializes a CBOR-encoded block with era tagging into the appropriate block type.
    /// </summary>
    /// <param name="blockCbor">The raw CBOR bytes of the era-tagged block.</param>
    /// <returns>The deserialized block, or null if deserialization fails.</returns>
    public static Block? DeserializeBlockWithEra(ReadOnlyMemory<byte> blockCbor)
    {
        CborReader reader = new(blockCbor, CborConformanceMode.Lax);

        // N2C format: Tag(24, ByteString([era, block]))
        // Read and verify tag 24
        CborTag tag = reader.ReadTag();
        if (tag != CborTag.EncodedCborDataItem)
        {
            throw new InvalidOperationException($"Expected CBOR tag 24, got {tag}");
        }

        // Read the byte string containing [era, block]
        byte[] innerBytes = reader.ReadByteString();

        // Now read the actual array from the inner bytes
        reader = new CborReader(innerBytes, CborConformanceMode.Lax);
        _ = reader.ReadStartArray();
        Era era = (Era)reader.ReadInt32();
        ReadOnlyMemory<byte> blockBytes = reader.ReadEncodedValue(true);

        return era switch
        {
            Era.Shelley or Era.Allegra or Era.Mary or Era.Alonzo => AlonzoCompatibleBlock.Read(blockBytes),
            Era.Babbage => BabbageBlock.Read(blockBytes),
            Era.Conway => ConwayBlock.Read(blockBytes),
            Era.Unknown => throw new NotImplementedException(),
            Era.Byron => throw new NotImplementedException(),
            _ => throw new NotSupportedException($"Unsupported era: {era}")
        };
    }
}
