namespace Argus.Sync.Data.Models;

public record TransactionOutput : IReducerModel
{
    public string Id { get; init; } = default!;
    public uint Index { get; init; }
    public string Address { get; init; } = default!;
    public Value Amount { get; init; } = default!;
    public Datum? Datum { get; init; }
    public ulong Slot { get; init; }
}