namespace Argus.Sync.Data.Models;

public record TxBySlot(
    string Hash,
    ulong Slot,
    uint Index,
    byte[] RawCbor
) : IReducerModel;