using Argus.Sync.Data.Models;
using Argus.Sync.Data.Models.Enums;

namespace Argus.Sync.Example.Models;

public record PriceBySubject(
    string OutRef,
    ulong Slot,
    ulong? SpentSlot,
    string Subject,
    ulong Price,
    UtxoStatus Status
) : IReducerModel;