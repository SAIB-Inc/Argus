// using TxOutput = Cardano.Sync.Data.Models.Datums.TransactionOutput;
using TransactionOutputEntity = Argus.Sync.Data.Models.TransactionOutput;
using Chrysalis.Cbor;
using Argus.Sync.Data.Models.Enums;
using TransactionOutput = Chrysalis.Cardano.Models.Core.Transaction.TransactionOutput;
// using ValueEntity = Argus.Sync.Data.Models.Value;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Models.Core;

namespace Argus.Sync.Utils;

public static class DataUtils
{
    public static TransactionOutputEntity? MapTransactionOutputEntity(string TransactionId, uint outputIndex, ulong slot, TransactionOutput output, UtxoStatus status)
    {
        string? address = output.Address().Value.ToBech32();
        
        if (address == null)
            return null;

        Datum? datum = output.GetDatumInfo() is var datumInfo && datumInfo.HasValue
            ? new Datum(datumInfo.Value.Type, datumInfo.Value.Value)
            : null;

        return new TransactionOutputEntity
        {
            Id = TransactionId,
            Address = address,
            Slot = slot,
            Index = outputIndex,
            Datum = datum,
            Amount = output.Amount(),
            ReferenceScript = output.ScriptRef(),
            UtxoStatus = status,
        };
    }
}