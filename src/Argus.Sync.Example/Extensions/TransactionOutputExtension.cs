using System.Formats.Cbor;
using Argus.Sync.Example.Models.Enums;
using CborSerialization;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Argus.Sync.Example.Extensions;

public enum DatumType
{
    Inline,
    Hash,
    None
}

public static class TransactionOutputExtension
{
    public static byte[]? Datum(this TransactionOutput transactionOutput)
    {
        (DatumType DatumType, byte[]? RawData) datum = transactionOutput.DatumInfo();

        if (datum.DatumType != DatumType.Inline)
            return datum.RawData;

        CborReader reader = new(datum.RawData);
        reader.ReadTag();
        ReadOnlyMemory<byte> blockBytes = reader.ReadByteString();

        return blockBytes.ToArray();
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
                InlineDatumOption inline => (DatumType.Inline, inline.Data()),
                DatumHashOption hash => (DatumType.Hash, hash.DatumHash),
                _ => (DatumType.None, null)
            },
            _ => (DatumType.None, null)
        };
    }
}