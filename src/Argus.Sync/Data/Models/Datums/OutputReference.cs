using System.Formats.Cbor;
using Argus.Sync.Data.Models.Datums;
using CborSerialization;

namespace Argus.Sync.Data.Models.Datums;


[CborSerialize(typeof(OutputReferenceCborConvert))]
public record OutputReference(byte[] TransactionId, ulong OutputIndex) : IDatum;
public class OutputReferenceCborConvert : ICborConvertor<OutputReference>
{
    public OutputReference Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }

        reader.ReadStartArray();
        tag = reader.ReadTag();

        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }

        reader.ReadStartArray();
        var transactionId = reader.ReadByteString();
        reader.ReadEndArray();
        var outputIndex = reader.ReadUInt64();
        reader.ReadEndArray();
        return new OutputReference(transactionId, outputIndex);
    }

    public void Write(ref CborWriter writer, OutputReference value)
    {
        writer.WriteTag((CborTag)121); // Adjust the tag number as necessary
        writer.WriteStartArray(null); // Start the outer array
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(null);
        writer.WriteByteString(value.TransactionId);
        writer.WriteEndArray();
        writer.WriteUInt64(value.OutputIndex);
        writer.WriteEndArray(); // End the outer array
    }
}