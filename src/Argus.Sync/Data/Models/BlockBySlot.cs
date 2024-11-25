namespace Argus.Sync.Data.Models;

public record BlockBySlot(
    ulong Slot, 
    string Hash, 
    byte[] RawCbor
) : IReducerModel;