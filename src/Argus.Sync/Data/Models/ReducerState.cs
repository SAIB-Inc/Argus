namespace Argus.Sync.Data.Models;

public record ReducerState(string Name, ulong Slot, string Hash, DateTimeOffset CreatedAt)
{
    public string Name { get; set; } = Name;
    public ulong Slot { get; set; } = Slot;
    public string Hash { get; set; } = Hash;
    public DateTimeOffset CreatedAt { get; set; } = CreatedAt;
}