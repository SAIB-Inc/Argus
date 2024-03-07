using System.Formats.Cbor;
using System.Text;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

/*
CBOR Diagnostic Notation (RFC 8949)
{
    h'6c6f636b65645f616d6f756e74': h'31303030',
    h'6e616d65': h'5374616b65204e465420314b20434e4354202d20323430313233',
}
JSON
{
    "locked_amount": "1000",
    "name": "Stake NFT 1K CNCT - 240123"
}
TypeScript
const timelockMetadata: TimeLockMetadata = new Map([
    [fromText("locked_amount"), fromText(1000n.toString())],
    [fromText("name"), fromText("Stake NFT 1K CNCT - 240123")]
]);
*/
[CborSerialize(typeof(CIP68MetdataCborConvert))]
public record CIP68Metdata(Dictionary<string, string> Data) : IDatum;

public class CIP68MetdataCborConvert : ICborConvertor<CIP68Metdata>
{
    public CIP68Metdata Read(ref CborReader reader)
    {
        reader.ReadStartMap();
        var metadata = new Dictionary<string, string>();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = Encoding.UTF8.GetString(reader.ReadByteString());
            var value = Encoding.UTF8.GetString(reader.ReadByteString());
            metadata.Add(key, value);
        }
        reader.ReadEndMap();
        return new CIP68Metdata(metadata);
    }

    public void Write(ref CborWriter writer, CIP68Metdata value)
    {
        writer.WriteStartMap(value.Data.Count);
        foreach (var (key, v) in value.Data)
        {
            writer.WriteByteString(Encoding.UTF8.GetBytes(key));
            byte[] valueBytes = Encoding.UTF8.GetBytes(v);
            if (valueBytes.Length > 64) 
            {
                writer.WriteStartIndefiniteLengthByteString();
                for (int i = 0; i < valueBytes.Length; i += 64)
                {
                    int chunkSize = Math.Min(64, valueBytes.Length - i);
                    byte[] chunk = new byte[chunkSize];
                    Array.Copy(valueBytes, i, chunk, 0, chunkSize);
                    writer.WriteByteString(chunk);
                }
                writer.WriteEndIndefiniteLengthByteString();
            }
            else
            {
                writer.WriteByteString(valueBytes);
            }
        }
        writer.WriteEndMap();
    }
}