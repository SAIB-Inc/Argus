using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

[CborSerialize(typeof(ValueCborConvert))]
public record Value(ulong Lovelace, MultiAsset<ulong>? MultiAsset) : IDatum;

public class ValueCborConvert : ICborConvertor<Value>
{
    public Value Read(ref CborReader reader)
    {
        reader.ReadStartArray();
        ulong lovelace = reader.ReadUInt64();
        var datumConverter = (ICborConvertor<MultiAsset<ulong>>)CborConverter.GetConvertor(typeof(MultiAsset<ulong>));
        var multiAsset = datumConverter.Read(ref reader);
        reader.ReadEndArray();
        return new Value(lovelace, multiAsset);
    }

    public void Write(ref CborWriter writer, Value value)
    {
        writer.WriteStartArray(2);
        writer.WriteUInt64(value.Lovelace);
        var datumConverter = (ICborConvertor<MultiAsset<ulong>>)CborConverter.GetConvertor(typeof(MultiAsset<ulong>));
        if (value.MultiAsset is not null)
        {
            datumConverter.Write(ref writer, value.MultiAsset);
        }
        else
        {
            throw new NullReferenceException(nameof(value.MultiAsset));
        }
        writer.WriteEndArray();
    }
}