using Argus.Sync.Data.Models.Enums;
using TransactionOutput = Chrysalis.Cardano.Models.Core.Transaction.TransactionOutput;
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

        Datum? datum = output.GetDatumInfo() is var datumInfo && datumInfo.HasValue
            ? new Datum(datumInfo.Value.Type, datumInfo.Value.Data)
            : null;

        return new(
            transactionId,
            outputIndex,
            slot,
            null,
            address,
            CborSerializer.Serialize(output),
            datum,
            output.ScriptRef(),
            status
        );
    }
}