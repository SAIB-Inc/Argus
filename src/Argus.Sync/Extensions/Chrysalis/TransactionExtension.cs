using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cardano.Models.Core.Transaction;
using TransactionOutput = Chrysalis.Cardano.Models.Core.Transaction.TransactionOutput;
using Chrysalis.Cbor;
using Chrysalis.Utils;

namespace Argus.Sync.Extensions.Chrysalis;

public static class TransactionExtension
{
    public static IEnumerable<TransactionInput> Inputs(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Inputs switch
            {
                CborDefiniteList<TransactionInput> list => list.Value,
                CborIndefiniteList<TransactionInput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody x => x.Inputs switch
            {
                CborDefiniteList<TransactionInput> list => list.Value,
                CborIndefiniteList<TransactionInput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionBody x => x.Inputs switch
            {
                CborDefiniteList<TransactionInput> list => list.Value,
                CborIndefiniteList<TransactionInput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<TransactionOutput> Outputs(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Outputs switch
            {
                CborDefiniteList<TransactionOutput> list => list.Value,
                CborIndefiniteList<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody x => x.Outputs switch
            {
                CborDefiniteList<TransactionOutput> list => list.Value,
                CborIndefiniteList<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionBody x => x.Outputs switch
            {
                CborDefiniteList<TransactionOutput> list => list.Value,
                CborIndefiniteList<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static (Address Address, Value Amount) GetComponents(this TransactionOutput output)
        => output switch
        {
            BabbageTransactionOutput x => (x.Address, x.Amount),
            AlonzoTransactionOutput x => (x.Address, x.Amount),
            MaryTransactionOutput x => (x.Address, x.Amount),
            _ => throw new NotImplementedException($"Unsupported TransactionOutput type: {output.GetType().Name}")
        };

    public static Address TransactionOutputAddress(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Address,
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Address,
            MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Address,
            ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Address,
            _ => throw new NotImplementedException()
        };

    public static Value TransactionOutputAmount(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Amount,
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount,
            MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Amount,
            ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Amount,
            _ => throw new NotImplementedException()
        };

    public static byte[]? TransactionOutputScriptRef(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput?.ScriptRef?.Value,
            _ => null
        };

    public static DatumOption? TransactionOutputDatumOption(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Datum,
            _ => null
        };

    public static byte[]? TransactionOutputDatumHash(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.DatumHash.Value,
            _ => null
        };

    public static (DatumType Type, byte[] Value)? GetDatumInfo(this TransactionOutput transactionOutput)
    {
        var datumOption = transactionOutput.TransactionOutputDatumOption();

        if (datumOption == null)
        {
            byte[]? datumHash = transactionOutput.TransactionOutputDatumHash();
            return datumHash != null ? (DatumType.DatumHash, datumHash) : null;
        }

        return datumOption switch
        {
            DatumHashOption hashOption => (DatumType.DatumHash, hashOption.DatumHash.Value),
            InlineDatumOption inlineOption => (DatumType.InlineDatum, CborSerializer.Serialize(inlineOption.Data)),
            _ => throw new NotImplementedException($"Unsupported DatumOption type: {datumOption.GetType().Name}")
        };
    }
}
