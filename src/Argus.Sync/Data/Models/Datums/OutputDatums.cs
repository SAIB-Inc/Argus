using System.Formats.Cbor;
using CborSerialization;

namespace Argus.Sync.Data.Models.Datums;

/*
121_0([])
*/
[CborSerialize(typeof(NoDatumCborConvert))]
public record NoDatum() : IDatum;

public class NoDatumCborConvert : ICborConvertor<NoDatum>
{
    public NoDatum Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }
        reader.ReadStartArray();
        reader.ReadEndArray();
        return new NoDatum();
    }

    public void Write(ref CborWriter writer, NoDatum value)
    {
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(0);
        writer.WriteEndArray();
    }
}

/*
122_0([_
    h'6c00ac8ecdbfad86c9287b2aec257f2e3875b572de8d8df27fd94dd650671c94',
])
*/
[CborSerialize(typeof(DatumHashCborConvert))]
public record DatumHash(byte[] Hash) : IDatum;

public class DatumHashCborConvert : ICborConvertor<DatumHash>
{
    public DatumHash Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 122)
        {
            throw new Exception("Invalid tag");
        }
        reader.ReadStartArray();
        var hash = reader.ReadByteString();
        reader.ReadEndArray();
        return new DatumHash(hash);
    }

    public void Write(ref CborWriter writer, DatumHash value)
    {
        writer.WriteTag((CborTag)122);
        writer.WriteStartArray(null);
        writer.WriteByteString(value.Hash);
        writer.WriteEndArray();
    }
}

/*
Super dynamic Datum that can contain any other Datum

This is an example of InlineDatum with a Credential Datum inside it (in Cbor Diagnostic Notation)

123_0([_
    121_0([_
        h'cb84310092f8c3dae1ebf0ac456114e487297d3fe684d3236588d5b3',
    ]),
])
*/
[CborSerialize(typeof(InlineDatumCborConvert<>))]
public record InlineDatum<T>(T Datum) : IDatum where T : IDatum;

public class InlineDatumCborConvert<T> : ICborConvertor<InlineDatum<T>> where T : IDatum
{
    public InlineDatum<T> Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 123) // Ensure it's the correct tag for InlineDatum
            throw new InvalidOperationException("Unexpected CBOR tag, expected 123 for InlineDatum.");

        reader.ReadStartArray();

        // Directly use the converter for T, since we know the type at compile time
        var datumConverter = (ICborConvertor<T>)CborConverter.GetConvertor(typeof(T));
        var datum = datumConverter.Read(ref reader);

        reader.ReadEndArray();

        return new InlineDatum<T>(datum);
    }

    public void Write(ref CborWriter writer, InlineDatum<T> value)
    {
        writer.WriteTag((CborTag)123); // Tag for InlineDatum
        writer.WriteStartArray(null); // One item in the array, the datum

        // Directly use the converter for T
        var datumConverter = (ICborConvertor<T>)CborConverter.GetConvertor(typeof(T));
        datumConverter.Write(ref writer, value.Datum);

        writer.WriteEndArray();
    }
}


