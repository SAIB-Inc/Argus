using Argus.Sync.Data.Models;
using Argus.Sync.Example.Models.Enums;

namespace Argus.Sync.Example.Models;

public record OrderBySlot(
    string TxHash,
    ulong TxIndex,
    ulong Slot,
    string OwnerAddress,
    string PolicyId,
    string AssetName,
    ulong Quantity,
    ulong? SpentSlot,
    string? BuyerAddress,
    string? SpentTxHash,
    byte[] RawData,
    byte[]? DatumRaw,
    OrderStatus Status
) : IReducerModel;