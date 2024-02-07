namespace Cardano.Sync.Data.Models;

public record ReducerState
{
    public string Name { get; set; } = default!;
    public ulong Slot { get; set; }
    public string Hash { get; set; } = default!;
}