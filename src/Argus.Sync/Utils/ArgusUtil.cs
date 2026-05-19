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
        // Defensive copy: Chrysalis IBlock lazy-decodes from a ReadOnlyMemory<byte>
        // reference. Chrysalis' multi-segment ChannelBuffer path can compact
        // bytes in-place while downstream Argus reducers still hold prior
        // decoded blocks. With the channel-pipeline architecture, downstream
        // reducers can dequeue an IBlock long after the chain consumer has pulled
        // the next block; owning the bytes here
        // guarantees stable decoding for the lifetime of the IBlock.
        byte[] owned = blockCbor.ToArray();
        ReadOnlyMemory<byte> ownedMemory = owned;

        ReadOnlyMemory<byte> innerBytes;
        CborReader probe = new(ownedMemory, CborConformanceMode.Lax);
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
            innerBytes = ownedMemory;
        }

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(innerBytes);
        return blockWithEra.Block;
    }
}
