using System.Formats.Cbor;
using CborSerialization;

namespace Argus.Sync.Data.Models.Datums;

/*
Object with two fields Policy Id and Asset Name
121_0([_
        h'1f164eea5c242f53cb2df2150fa5ab7ba126350e904ddbcc65226e18',
        h'634e4554415f4144415f504f4f4c5f4944454e54495459',
    ]),
*/
[CborSerialize(typeof(AssetClassCborConvert))]
public record AssetClass(byte[] PolicyId, byte[] AssetName) : IDatum;

public class AssetClassCborConvert : ICborConvertor<AssetClass>
{
    public AssetClass Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }
        reader.ReadStartArray();
        var policyId = reader.ReadByteString();
        var assetName = reader.ReadByteString();
        reader.ReadEndArray();
        return new AssetClass(policyId, assetName);
    }

    public void Write(ref CborWriter writer, AssetClass value)
    {
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(null);
        writer.WriteByteString(value.PolicyId);
        writer.WriteByteString(value.AssetName);
        writer.WriteEndArray();
    }
}
