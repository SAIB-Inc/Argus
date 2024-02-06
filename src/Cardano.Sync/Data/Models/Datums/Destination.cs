
using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

[CborSerialize(typeof(DestinationCborConvert<>))]
public record Destination<T>(Address Address, T Datum) : IDatum;

public class DestinationCborConvert<T> : ICborConvertor<Destination<T>> where T : IDatum
{
    public Destination<T> Read(ref CborReader reader)
    {
        // Assuming there's a starting tag for Destination
        var tag = reader.ReadTag();
        if ((int)tag != 121)
            throw new InvalidOperationException("Unexpected CBOR tag, expected tag for Destination.");

        reader.ReadStartArray();

        // Deserialize the Address
        var addressConvertor = new AddressCborConvert();
        var address = addressConvertor.Read(ref reader);

        // Deserialize the Datum of type T
        var datumConverter = (ICborConvertor<T>)CborConverter.GetConvertor(typeof(T));
        var datum = datumConverter.Read(ref reader);

        reader.ReadEndArray();

        return new Destination<T>(address, datum);
    }

    public void Write(ref CborWriter writer, Destination<T> value)
    {
        writer.WriteTag((CborTag)121); 
        writer.WriteStartArray(null); // Start of Destination array

        // Serialize the Address
        var addressConvertor = new AddressCborConvert();
        addressConvertor.Write(ref writer, value.Address);

        // Serialize the Datum of type T
        var datumConverter = (ICborConvertor<T>)CborConverter.GetConvertor(typeof(T));
        datumConverter.Write(ref writer, value.Datum);

        writer.WriteEndArray(); // End of Destination array
    }
}
