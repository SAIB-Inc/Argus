namespace Argus.Sync.Data.Models;

public record Block
(
    string Hash,
    ulong Slot,
    byte[]? Cbor
);

