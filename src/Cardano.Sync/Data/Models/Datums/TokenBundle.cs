using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

[CborSerialize(typeof(TokenBundleCborConvert<>))]
public record TokenBundle<T>(Dictionary<byte[], T> Bundle) : IDatum where T : struct, System.Numerics.INumber<T>;

public class TokenBundleCborConvert<T> : ICborConvertor<TokenBundle<T>> where T : struct, System.Numerics.INumber<T>
{
    public TokenBundle<T> Read(ref CborReader reader)
    {
        reader.ReadStartMap();
        var bundle = new Dictionary<byte[], T>();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadByteString();
            // @TODO handle other number types thats supported by CBOR
            if (typeof(T) == typeof(long))
            {
                bundle.Add(key, T.CreateChecked(reader.ReadInt64()));
            }
            else if (typeof(T) == typeof(ulong))
            {
                bundle.Add(key, T.CreateChecked(reader.ReadUInt64()));
            }
            else
            {
                throw new NotImplementedException();
            }
        }
        reader.ReadEndMap();
        return new TokenBundle<T>(bundle);
    }

    public void Write(ref CborWriter writer, TokenBundle<T> value)
    {
        writer.WriteStartMap(value.Bundle.Count);
        foreach (var (key, val) in value.Bundle)
        {
            writer.WriteByteString(key);

            if (typeof(T) == typeof(long))
            {
                writer.WriteInt64(Convert.ToInt64(val));
            }
            else if (typeof(T) == typeof(ulong))
            {
                writer.WriteUInt64(Convert.ToUInt64(val));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        writer.WriteEndMap();
    }
}
