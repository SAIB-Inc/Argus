using Argus.Sync.Data.Models;
using Argus.Sync.Data.Models.Enums;

namespace Argus.Sync.Example.Models;

public record UtxoByAddress(
    string TxHash,
    ulong TxIndex,
    ulong Slot,
    string Address,
    ulong BlockNumber,
    UtxoStatus Status,
    ulong? SpentSlot,
    byte[]? Raw
) : IReducerModel;