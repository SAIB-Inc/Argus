namespace Argus.Sync.Data.Models;

public record class Block(
    string Id,
    ulong Number,
    ulong Slot
) : IReducerModel;