
using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

[CborSerialize(typeof(SignatureCborConvert))]
public record Signature(byte[] KeyHash) : IDatum;

public class SignatureCborConvert : ICborConvertor<Signature>
{
    public Signature Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }
        reader.ReadStartArray();
        var keyHash = reader.ReadByteString();
        reader.ReadEndArray();
        return new Signature(keyHash);
    }

    public void Write(ref CborWriter writer, Signature value)
    {
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(null);
        writer.WriteByteString(value.KeyHash);
        writer.WriteEndArray();
    }
}
