using System.Formats.Cbor;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;

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
    /// Deserializes a CBOR-encoded block into the appropriate block type, accepting either
    /// the tag-wrapped on-wire form <c>Tag(24, ByteString([era, block]))</c> or the inner
    /// <c>[era, block]</c> form (which Chrysalis' <c>CborEncodedValue</c> already produces).
    /// </summary>
    /// <param name="blockCbor">The CBOR bytes of the block, with or without the outer tag-24 wrap.</param>
    /// <returns>The deserialized block.</returns>
    public static IBlock? DeserializeBlockWithEra(ReadOnlyMemory<byte> blockCbor)
    {
        ReadOnlyMemory<byte> innerBytes;
        CborReader probe = new(blockCbor, CborConformanceMode.Lax);
        if (probe.PeekState() == CborReaderState.Tag)
        {
            CborTag tag = probe.ReadTag();
            if (tag != CborTag.EncodedCborDataItem)
            {
                throw new InvalidOperationException($"Expected CBOR tag 24, got {tag}");
            }
            innerBytes = probe.ReadByteString();
        }
        else
        {
            innerBytes = blockCbor;
        }

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(innerBytes);
        return blockWithEra.Block;
    }
}
