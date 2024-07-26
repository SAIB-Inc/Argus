using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums.SundaeSwap;

[CborSerialize(typeof(AssetClassCborConverter))]
public record AssetClass(ByteArray PolicyId, ByteArray AssetName)
    : Tuple<ByteArray, ByteArray>(PolicyId, AssetName);

public class AssetClassCborConverter : ICborConvertor<AssetClass>
{
    public AssetClass Read(ref CborReader reader)
    {
        var tupleConvert = new TupleCborConverter<ByteArray, ByteArray>();
        var tuple = tupleConvert.Read(ref reader);
        return new AssetClass(tuple.First, tuple.Second);
    }

    public void Write(ref CborWriter writer, AssetClass value)
    {
        var tupleConvert = new TupleCborConverter<ByteArray, ByteArray>();
        var tuple = new Tuple<ByteArray, ByteArray>(value.PolicyId, value.AssetName);
        tupleConvert.Write(ref writer, tuple);
    }
}