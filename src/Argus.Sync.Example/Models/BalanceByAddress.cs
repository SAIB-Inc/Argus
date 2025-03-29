using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record BalanceByAddress(
    string Address,
    ulong Balance
) : IReducerModel;