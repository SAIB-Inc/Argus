using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Extensions.Chrysalis;
using Argus.Sync.Data.Models;
using Chrysalis.Cbor;
using Chrysalis.Cardano.Core;

namespace Argus.Sync.Utils;

public static class DataUtils
{
    public static OutputBySlot? MapTransactionOutputEntity(string transactionId, uint outputIndex, ulong slot, TransactionOutput output, UtxoStatus status)
    {
        string? address = output.AddressValue().ToBech32();

        if (address == null)
            return null;

        Datum? datum = output.ArgusDatumInfo() is var datumInfo && datumInfo.HasValue
            ? new (datumInfo.Value.Type, datumInfo.Value.Data)
            : null;

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