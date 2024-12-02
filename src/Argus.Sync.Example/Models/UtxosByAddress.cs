using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record UtxosByAddress(
    string TxHash,
    int TxIndex,
    ulong Slot,
    string Address,
    ulong Amount
) : IReducerModel;