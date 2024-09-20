using System.Formats.Cbor;
using System.Text;
using CborSerialization;

namespace Argus.Sync.Data.Models.Datums;

/*
121_0([_
    {
        h'6c6f636b65645f616d6f756e74': h'31303030',
        h'6e616d65': h'5374616b65204e465420314b20434e4354202d20323430313233',
    },
    1,
    121_0([_
        1000_1,
        h'6c00ac8ecdbfad86c9287b2aec257f2e3875b572de8d8df27fd94dd650671c94',
    ]),
])
*/
[CborSerialize(typeof(CIP68CborConvert<>))]
public record CIP68<T>(CIP68Metdata Metadata, ulong Version, T Extra) : IDatum where T : IDatum;

public class CIP68CborConvert<T> : ICborConvertor<CIP68<T>> where T : IDatum
{
    public CIP68<T> Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }

        reader.ReadStartArray();

        var metadata = new CIP68MetdataCborConvert().Read(ref reader);
        var version = reader.ReadUInt64();

        var datumConverter = (ICborConvertor<T>)CborConverter.GetConvertor(typeof(T));
        var extra = datumConverter.Read(ref reader);

        reader.ReadEndArray();

        return new CIP68<T>(metadata, version, extra);
    }

    public void Write(ref CborWriter writer, CIP68<T> value)
    {
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(null);

        new CIP68MetdataCborConvert().Write(ref writer, value.Metadata);
        writer.WriteUInt64(value.Version);

        var datumConverter = (ICborConvertor<T>)CborConverter.GetConvertor(typeof(T));
        datumConverter.Write(ref writer, value.Extra);

        writer.WriteEndArray();
    }
}