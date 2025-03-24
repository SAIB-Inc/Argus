using Argus.Sync.Data.Models;
using Argus.Sync.Example.Data.Enum;

namespace Argus.Sync.Example.Models;

public record OrderBySlot(
    string TxHash,
    ulong Index,
    ulong Slot,
    string OwnerAddress,
    string PolicyId,
    string AssetName,
    ulong Quantity,
    string? BuyerAddress,
    string? SpentTxHash,
    byte[] RawData,
    byte[]? DatumRaw,
    OrderStatus Status
) : IReducerModel;