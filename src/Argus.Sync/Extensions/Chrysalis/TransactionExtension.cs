using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cbor;

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
            ConwayTransactionBody x => ParseOutputs(x.Outputs),
            BabbageTransactionBody x => ParseOutputs(x.Outputs),
            AlonzoTransactionBody x => ParseOutputs(x.Outputs),
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

    private static IEnumerable<TransactionOutput> ParseOutputs(ICbor outputs)
        => outputs switch
        {
            CborDefiniteList<BabbageTransactionOutput> list => list.Value,
            CborIndefiniteList<BabbageTransactionOutput> list => list.Value,
            CborDefiniteList<AlonzoTransactionOutput> list => list.Value,
            CborIndefiniteList<AlonzoTransactionOutput> list => list.Value,
            CborDefiniteList<MaryTransactionOutput> list => list.Value,
            CborIndefiniteList<MaryTransactionOutput> list => list.Value,
            CborDefiniteList<ShellyTransactionOutput> list => list.Value,
            CborIndefiniteList<ShellyTransactionOutput> list => list.Value,
            _ => throw new NotImplementedException($"Unsupported output type: {outputs.GetType().Name}")
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
            _ => throw new NotImplementedException()
        };
}
