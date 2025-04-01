using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record UtxoByAddress(
    string TxHash,
    int TxIndex,
    ulong Slot,
    string Address,
    ulong BlockNumber,
    byte[] RawData
) : IReducerModel;