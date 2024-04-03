using System.Formats.Cbor;
using Cardano.Sync.Data.Models.Datums;
using CardanoSharp.Wallet.Extensions.Models.Transactions;
using CborSerialization;
using PeterO.Cbor2;
using SharpTransactionOutput = CardanoSharp.Wallet.Models.Transactions.TransactionOutput;

namespace Cardano.Sync.Data.Models.Experimental;

[CborSerialize(typeof(TransactionOutputConvert))]
public record TransactionOutput(SharpTransactionOutput Value): IDatum;

public class TransactionOutputConvert : ICborConvertor<TransactionOutput>
{
    public TransactionOutput Read(ref CborReader reader)
    {
        var value = reader.ReadEncodedValue();
        var sharpOutput = CBORObject.DecodeFromBytes(value.Span.ToArray()).GetTransactionOutput();
        return new TransactionOutput(sharpOutput);
    }

    public void Write(ref CborWriter writer, TransactionOutput value)
    {
        var encodedValue = value.Value.GetCBOR().EncodeToBytes();
        writer.WriteEncodedValue(encodedValue);
    }
}