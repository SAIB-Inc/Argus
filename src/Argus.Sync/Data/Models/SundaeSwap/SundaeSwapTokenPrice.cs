namespace Argus.Sync.Data.Models.SundaeSwap;

public record SundaeSwapTokenPrice : IReducerModel
{
    public string TokenXSubject { get; init; } = default!;
    public string TokenYSubject { get; init; } = default!;
    public string TxHash { get; init; } = default!;
    public ulong TokenXPrice { get; init; }
    public ulong TokenYPrice { get; init; }
    public ulong Slot { get; init; }
    public ulong TxIndex { get; init; }
};