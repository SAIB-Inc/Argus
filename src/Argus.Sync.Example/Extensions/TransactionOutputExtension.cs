using Argus.Sync.Example.Models.Enums;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Argus.Sync.Example.Extensions;

public static class TransactionOutputExtension
{
    public static byte[]? Datum(this TransactionOutput transactionOutput)
    {
        (DatumType DatumType, byte[]? RawData) datum = transactionOutput.DatumInfo();

        return datum.RawData;
    }

    public static (DatumType DatumType, byte[]? RawData) DatumInfo(this TransactionOutput transactionOutput)
    {

        return transactionOutput switch
        {
            AlonzoTransactionOutput a => a.DatumHash switch
            {
                null => (DatumType.None, null),
                _ => (DatumType.Hash, a.DatumHash)
            },
            PostAlonzoTransactionOutput b => b.Datum switch
            {
                InlineDatumOption inline => (DatumType.Inline, inline.Data.Value),
                DatumHashOption hash => (DatumType.Hash, hash.DatumHash),
                _ => (DatumType.None, null)
            },
            _ => (DatumType.None, null)
        };
    }
}