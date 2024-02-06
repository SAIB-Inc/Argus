using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

/*
121_0([_
    121_0([_
        h'cb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3',
    ]),
])
*/
[CborSerialize(typeof(StakeCredentialCborConvert))]
public record StakeCredential(Credential Credential) : IDatum;

public class StakeCredentialCborConvert : ICborConvertor<StakeCredential>
{
    public StakeCredential Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }
        reader.ReadStartArray();
        var credential = new CredentialCborConvert().Read(ref reader);
        reader.ReadEndArray();
        return new StakeCredential(credential);
    }

    public void Write(ref CborWriter writer, StakeCredential value)
    {
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(null);
        new CredentialCborConvert().Write(ref writer, value.Credential);
        writer.WriteEndArray();
    }
}