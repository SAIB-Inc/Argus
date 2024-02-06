using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

[CborSerialize(typeof(CredentialCborConvert))]
public record Credential(byte[] Hash) : IDatum;

/*
121_0([_
    h'cb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3',
])
*/
public class CredentialCborConvert : ICborConvertor<Credential>
{
    public Credential Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }
        reader.ReadStartArray();
        var hash = reader.ReadByteString();
        reader.ReadEndArray();
        return new Credential(hash);
    }

    public void Write(ref CborWriter writer, Credential value)
    {
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(null);
        writer.WriteByteString(value.Hash);
        writer.WriteEndArray();
    }
}