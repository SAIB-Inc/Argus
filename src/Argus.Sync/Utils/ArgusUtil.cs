using Argus.Sync.Data.Models;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Network.Cbor.Common;
using SAIB.Cbor.Serialization;

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

        // Peel an optional CBOR tag-24 (encoded-CBOR-data-item) wrapper using the SAIB.Cbor
        // reader that already ships with Chrysalis: TryReadSemanticTag consumes the tag only
        // when present, leaving the bare [era, block] form (what CborEncodedValue / the
        // ChannelBuffer deliver) untouched.
        ReadOnlyMemory<byte> innerBytes;
        CborReader probe = new(ownedMemory.Span);
        if (probe.TryReadSemanticTag(out ulong tag))
        {
            if (tag != 24)
            {
                throw new InvalidOperationException($"Expected CBOR tag 24, got {tag}");
            }
            innerBytes = probe.ReadByteString().ToArray();
        }
        else
        {
            innerBytes = ownedMemory;
        }

        BlockWithEra blockWithEra = CborSerializer.Deserialize<BlockWithEra>(innerBytes);
        return blockWithEra.Block;
    }

    /// <summary>
    /// Maps a Chrysalis ChainSync rollback point to an Argus rollback <see cref="NextResponse"/>,
    /// applying standard Ouroboros chain-sync semantics (verified against ouroboros-network's
    /// <c>Ouroboros.Network.Mock.Chain.rollback</c>, Pallas, and Dolos):
    /// <list type="bullet">
    /// <item><description><b>SpecificPoint(X)</b> → <see cref="RollBackType.Exclusive"/>: the rollback
    /// point block is preserved (the consumer keeps block X and discards everything after it), so the
    /// worker deletes slots strictly greater than X.</description></item>
    /// <item><description><b>OriginPoint</b> → <see cref="RollBackType.Inclusive"/> at slot 0: a rollback
    /// to genesis discards the entire chain, so the worker deletes every slot (≥ 0).</description></item>
    /// </list>
    /// Shared by the N2C and N2N providers; both speak standard Ouroboros chain-sync, where a rollback
    /// always keeps the rollback point itself.
    /// </summary>
    /// <param name="rollbackPoint">The point carried by a <c>MessageRollBackward</c>.</param>
    /// <returns>A roll-back <see cref="NextResponse"/> with the correct rollback type and slot.</returns>
    /// <exception cref="InvalidOperationException">The point is neither a specific point nor the origin.</exception>
    public static NextResponse RollBackwardResponse(Chrysalis.Network.Cbor.Common.Point rollbackPoint)
    {
        ArgumentNullException.ThrowIfNull(rollbackPoint);
        return rollbackPoint switch
        {
            SpecificPoint specific => new NextResponse(NextResponseAction.RollBack, RollBackType.Exclusive, null, specific.Slot),
            OriginPoint => new NextResponse(NextResponseAction.RollBack, RollBackType.Inclusive, null, 0UL),
            _ => throw new InvalidOperationException($"Unsupported ChainSync rollback point type: {rollbackPoint.GetType().Name}")
        };
    }
}
