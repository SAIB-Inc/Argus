using Chrysalis.Cardano.Models.Core;
using Argus.Sync.Data.Models.Enums;
using Chrysalis.Cbor;

namespace Argus.Sync.Data.Models;

public record OutputBySlot(
    string Id,
    uint Index,
    ulong Slot,
    ulong? SpentSlot,
    string Address,
    byte[] RawCbor,
    Datum? Datum,
    byte[]? ReferenceScript,
    UtxoStatus UtxoStatus
) : IReducerModel
{
    public Value Amount
    {
        get => CborSerializer.Deserialize<Value>(RawCbor) 
            ?? throw new InvalidOperationException("Failed to deserialize Value from RawCbor");
    }
}