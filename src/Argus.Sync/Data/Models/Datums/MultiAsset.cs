using System.Formats.Cbor;
using CborSerialization;

namespace Argus.Sync.Data.Models.Datums;

[CborSerialize(typeof(MultiAssetCborConvert<>))]
public record MultiAsset<T>(Dictionary<byte[], TokenBundle<T>> Assets) : IDatum where T : struct, System.Numerics.INumber<T>;

public class MultiAssetCborConvert<T> : ICborConvertor<MultiAsset<T>> where T : struct, System.Numerics.INumber<T>
{
    public MultiAsset<T> Read(ref CborReader reader)
    {
        reader.ReadStartMap();
        var assets = new Dictionary<byte[], TokenBundle<T>>();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadByteString();
            var datumConverter = (ICborConvertor<TokenBundle<T>>)CborConverter.GetConvertor(typeof(TokenBundle<T>));
            var value = datumConverter.Read(ref reader);
            assets.Add(key, value);
        }
        reader.ReadEndMap();
        return new MultiAsset<T>(assets);
    }

    public void Write(ref CborWriter writer, MultiAsset<T> value)
    {
        writer.WriteStartMap(value.Assets.Count);
        foreach (var (key, val) in value.Assets)
        {
            writer.WriteByteString(key);
            var datumConverter = (ICborConvertor<TokenBundle<T>>)CborConverter.GetConvertor(typeof(TokenBundle<T>));
            datumConverter.Write(ref writer, val);
        }
        writer.WriteEndMap();
    }
}
