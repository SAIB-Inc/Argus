using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;


[CborSerialize(typeof(CardanoIntCborConvert<CardanoInt>))]
public record CardanoInt(ulong Value) : IDatum
{
    public int CompareTo(object? obj)
    {
        throw new NotImplementedException();
    }
}


public class CardanoIntCborConvert<T> : ICborConvertor<T> where T : CardanoInt
{
    public T Read(ref CborReader reader)
    {
        Type type = typeof(T);
        T? result = Activator.CreateInstance(type, reader.ReadUInt64()) as T ?? 
            throw new Exception("Failed to create instance of CardanoInt");
        return result;
    }

    public void Write(ref CborWriter writer, T value)
    {
        writer.WriteUInt64(value.Value);
    }
}
