using Argus.Sync.Data.Models;
using Argus.Sync.Example.Models.Enums;

namespace Argus.Sync.Example.Models;

public record PriceByToken(
    string OutRef,
    ulong Slot,
    string TokenXSubject,
    string TokenYSubject,
    ulong TokenXPrice,
    ulong TokenYPrice,
    TokenPricePlatformType PlatformType
) : IReducerModel;