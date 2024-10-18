namespace Argus.Sync.Data.Models.Splash;

public record SplashTokenPrice() : IReducerModel
{
    public ulong Slot { get; init; }
    public string TxHash { get; init; } = default!;
    public ulong TxIndex { get; init; }
    public string PolicyId { get; init; } = default!;
    public string AssetName { get; init; } = default!;
    public ulong Price { get; set; } = default!;
}
