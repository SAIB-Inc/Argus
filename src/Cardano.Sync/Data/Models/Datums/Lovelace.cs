using System.Formats.Cbor;
using Cardano.Sync.Data.Models.Datums;
using CborSerialization;

[CborSerialize(typeof(LovelaceCborConvert))]
public record Lovelace(ulong Value) : IDatum;

public class LovelaceCborConvert: ICborConvertor<Lovelace>
{
    public Lovelace Read(ref CborReader reader)
    {
        return new Lovelace(reader.ReadUInt64());
    }

    public void Write(ref CborWriter writer, Lovelace value)
    {
        writer.WriteUInt64(value.Value);
    }
}
