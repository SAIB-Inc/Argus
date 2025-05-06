using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record UtxoByAddress(
    string TxHash,
    ulong TxIndex,
    ulong Slot,
    string Address,
    ulong Amount
) : IReducerModel;