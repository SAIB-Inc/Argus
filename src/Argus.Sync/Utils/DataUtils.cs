using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cardano.Core.Extensions;

namespace Argus.Sync.Utils;

public static class DataUtils
{
    public static OutputBySlot? MapTransactionOutputEntity(string transactionId, uint outputIndex, ulong slot, TransactionOutput output, UtxoStatus status)
    {
        string? address = output.Address()?.ToBech32();

        if (address == null)
            return null;

        (DatumType DatumType, byte[]? RawData) datum = output.DatumInfo();

        if (datum.RawData is null)
            datum = new(DatumType.None, []);

        return new(
            transactionId,
            outputIndex,
            slot,
            null,
            address,
            CborSerializer.Serialize(output),
            datum.DatumType,
            datum.RawData,
            output.ScriptRef(),
            status
        );
    }
}