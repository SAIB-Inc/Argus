using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record Royalty(
    string PolicyId,
    string Address,
    decimal Share,
    ulong Slot
) : IReducerModel;