using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record TestDependency(
    string TxHash,
    ulong Slot
) : IReducerModel;