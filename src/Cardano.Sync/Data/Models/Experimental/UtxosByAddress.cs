using System.Formats.Cbor;
using Cardano.Sync.Data.Models.Datums;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Experimental;

[CborSerialize(typeof(UtxosByAddressConvert))]
public record UtxosByAddress(Dictionary<TransactionInput, TransactionOutput> Values) : IDatum;

public class UtxosByAddressConvert : ICborConvertor<UtxosByAddress>
{
    public UtxosByAddress Read(ref CborReader reader)
    {
        var values = new Dictionary<TransactionInput, TransactionOutput>();
        reader.ReadStartMap();
        while(reader.PeekState() != CborReaderState.EndMap)
        {
            var key = new TransactionInputConvert().Read(ref reader);
            var value = new TransactionOutputConvert().Read(ref reader);
            values.Add(key, value);
        }
        reader.ReadEndMap();
        return new UtxosByAddress(values);
    }

    public void Write(ref CborWriter writer, UtxosByAddress utxosByAddress)
    {
        writer.WriteStartMap(utxosByAddress.Values.Count);
        foreach (var (key, value) in utxosByAddress.Values)
        {
            new TransactionInputConvert().Write(ref writer, key);
            new TransactionOutputConvert().Write(ref writer, value);
        }
        writer.WriteEndMap();
    }
}