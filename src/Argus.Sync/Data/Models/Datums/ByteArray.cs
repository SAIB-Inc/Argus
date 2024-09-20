
using System.Formats.Cbor;
using CborSerialization;

namespace Argus.Sync.Data.Models.Datums;

[CborSerialize(typeof(ByteArrayCborConverter))]
public record ByteArray(byte[] Value) : IDatum;

public class ByteArrayCborConverter : ICborConvertor<ByteArray>
{
    public ByteArray Read(ref CborReader reader)
    {
        return new ByteArray(reader.ReadByteString());
    }

    public void Write(ref CborWriter writer, ByteArray value)
    {
        writer.WriteByteString(value.Value);
    }
}
