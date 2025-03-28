using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record TxBySlot(
    string TxHash,
    ulong Index,
    ulong Slot,
    byte[] Raw
) : IReducerModel;