using Argus.Sync.Data.Models.Enums;
using TransactionOutput = Chrysalis.Cardano.Models.Core.Block.Transaction.TransactionOutput;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Data.Models;
using Chrysalis.Cbor;

namespace Argus.Sync.Utils;

public static class DataUtils
{
    public static OutputBySlot? MapTransactionOutputEntity(string transactionId, uint outputIndex, ulong slot, TransactionOutput output, UtxoStatus status)
    {
        string? address = output.Address().Value.ToBech32();

        if (address == null)
            return null;

        Datum? datum = output.DatumInfo() is var datumInfo && datumInfo.HasValue
            ? new (datumInfo.Value.Type, datumInfo.Value.Data)
            : null;

        //go over the implementation of the Datum in OutputBySlot again and recheck what to do
        //because it seems like it would be better if data or type was null?
        if (datum == null)
            datum = new(DatumType.NoDatum, Array.Empty<byte>());

        return new(
            transactionId,
            outputIndex,
            slot,
            null,
            address,
            CborSerializer.Serialize(output),
            datum.Type,
            datum.Data,
            output.ScriptRef(),
            status
        );
    }
}