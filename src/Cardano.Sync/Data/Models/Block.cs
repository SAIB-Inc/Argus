namespace Cardano.Sync.Data.Models;

public record class Block (
    string Id,
    ulong Number,
    ulong Slot
);