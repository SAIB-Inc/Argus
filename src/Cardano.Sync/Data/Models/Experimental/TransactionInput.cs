using System.Formats.Cbor;
using Cardano.Sync.Data.Models.Datums;
using SharpTransactionInput = CardanoSharp.Wallet.Models.Transactions.TransactionInput;
using CborSerialization;
using PeterO.Cbor2;
using CardanoSharp.Wallet.Extensions.Models.Transactions;

namespace Cardano.Sync.Data.Models.Experimental;


[CborSerialize(typeof(TransactionInputConvert))]
public record TransactionInput(SharpTransactionInput Value): IDatum;


public class TransactionInputConvert : ICborConvertor<TransactionInput>
{
    public TransactionInput Read(ref CborReader reader)
    {
        var value = reader.ReadEncodedValue();
        var sharpInput = CBORObject.DecodeFromBytes(value.Span.ToArray()).GetTransactionInput();
        return new TransactionInput(sharpInput);
    }

    public void Write(ref CborWriter writer, TransactionInput value)
    {
        var encodedValue = value.Value.GetCBOR().EncodeToBytes();
        writer.WriteEncodedValue(encodedValue);
    }
}