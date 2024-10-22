using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Models.Core.Block.Transaction;
using Chrysalis.Cardano.Models.Core.Block.Transaction.Output;

namespace Argus.Sync.Extensions.Chrysalis;

public static class TransactionInputExtension
{
    public static string TransacationId(this TransactionInput transactionInput)
        => Convert.ToHexString(transactionInput.TransactionId.Value).ToLowerInvariant();

    public static ulong Index(this TransactionInput transactionInput)
        => transactionInput.Index.Value;

    public static byte[]? ScriptRef(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput?.ScriptRef?.Value,
            _ => null
        };

    public static DatumOption? DatumOption(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Datum,
            _ => null
        };

    public static byte[]? DatumHash(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.DatumHash.Value,
            _ => null
        };

    public static (DatumType Type, byte[] Data)? DatumInfo(this TransactionOutput transactionOutput)
    {
        var datumOption = transactionOutput.DatumOption();

        if (datumOption == null)
        {
            byte[]? datumHash = transactionOutput.DatumHash();
            return datumHash != null ? (DatumType.DatumHash, datumHash) : null;
        }

        return datumOption switch
        {
            DatumHashOption hashOption => (DatumType.DatumHash, hashOption.DatumHash.Value),
            InlineDatumOption inlineOption => (DatumType.InlineDatum, inlineOption.Data.Value),
            _ => throw new NotImplementedException($"Unsupported DatumOption type: {datumOption.GetType().Name}")
        };
    }
}