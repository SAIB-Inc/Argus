namespace Argus.Sync.Data.Models;

/// <summary>
/// Represents a point on the Cardano blockchain identified by a block hash and slot number.
/// </summary>
/// <param name="Hash">The block hash at this point.</param>
/// <param name="Slot">The slot number at this point.</param>
public record Point
(
    string Hash,
    ulong Slot
);
