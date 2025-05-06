using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record OutputBySlot(
    string TxHash,
    ulong TxIndex,
    ulong Slot,
    byte[] OutputRaw
) : IReducerModel;