using Argus.Sync.Utils;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using SAIB.Cbor.Serialization;

namespace Argus.Sync.Tests.Unit;

/// <summary>
/// Pins the defensive copy in <see cref="ArgusUtil.DeserializeBlockWithEra"/> (the <c>blockCbor.ToArray()</c>).
/// A Chrysalis <see cref="IBlock"/> lazy-decodes its fields from the memory it was handed; under the channel
/// pipeline a downstream reducer can decode an <see cref="IBlock"/> long after the chain consumer has moved on
/// and reused/compacted the source buffer. The defensive copy gives the block its own stable bytes.
///
/// The test feeds the <b>inner</b> (non-tag-wrapped) <c>[era, block]</c> form — the form Chrysalis'
/// <c>CborEncodedValue</c>/ChannelBuffer delivers, where <c>DeserializeBlockWithEra</c> keeps the input memory
/// directly (so the copy is the only protection). The tag-24 path is not used here because <c>ReadByteString</c>
/// already copies there, which would mask the bug. It corrupts the source buffer and only THEN decodes the
/// block-under-test (first access after corruption — decoding earlier could cache and hide the bug), comparing
/// against an independent clean deserialization. It fails the moment the defensive copy is removed.
/// </summary>
public sealed class BufferOwnershipTest
{
    [Fact]
    public void DeserializedBlock_OwnsItsBytes_SurvivingSourceBufferReuse()
    {
        string blocksDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "Blocks");
        string blockFile = Directory.GetFiles(blocksDir, "*.cbor").OrderBy(f => f, StringComparer.Ordinal).First();

        // Real test blocks are tag-24 wrapped; strip to the inner [era, block] form.
        byte[] inner = StripEraTag(File.ReadAllBytes(blockFile));

        // Two independent copies of the inner bytes: one we corrupt, one the oracle never touches.
        byte[] reusedBuffer = (byte[])inner.Clone();
        byte[] pristine = (byte[])inner.Clone();

        IBlock blockUnderTest = ArgusUtil.DeserializeBlockWithEra(reusedBuffer)
            ?? throw new InvalidOperationException("test block failed to deserialize");
        IBlock oracle = ArgusUtil.DeserializeBlockWithEra(pristine)
            ?? throw new InvalidOperationException("oracle block failed to deserialize");

        // Known-good values from the oracle, whose buffer is never mutated.
        string expectedHash = oracle.Header().Hash();
        ulong expectedSlot = oracle.Header().HeaderBody().Slot();
        ulong expectedNumber = oracle.Header().HeaderBody().BlockNumber();

        // Simulate Chrysalis' ChannelBuffer reusing/compacting the bytes the block came from.
        Array.Fill(reusedBuffer, (byte)0xFF);

        // First decode of blockUnderTest happens here, AFTER the corruption. With the defensive copy it reads
        // its own stable bytes and matches the oracle; without it, these read 0xFF and would differ or throw.
        Assert.Equal(expectedHash, blockUnderTest.Header().Hash());
        Assert.Equal(expectedSlot, blockUnderTest.Header().HeaderBody().Slot());
        Assert.Equal(expectedNumber, blockUnderTest.Header().HeaderBody().BlockNumber());
    }

    private static byte[] StripEraTag(byte[] blockCbor)
    {
        CborReader reader = new(blockCbor);
        return reader.TryReadSemanticTag(out ulong tag) && tag == 24
            ? reader.ReadByteString().ToArray()
            : blockCbor;
    }
}
