using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Core;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

namespace Argus.Sync.Extensions.Chrysalis;

public static class TransactionInputExtension
{
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

    public static (DatumType Type, byte[] Data)? ArgusDatumInfo(this TransactionOutput transactionOutput)
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