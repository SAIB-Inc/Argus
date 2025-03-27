using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Argus.Sync.Data.Models;

public record ReducerState(string Name, DateTimeOffset CreatedAt)
{
    public string Name { get; set; } = Name;
    public DateTimeOffset CreatedAt { get; set; } = CreatedAt;
    public string LatestIntersectionsJson { get; set; } = string.Empty;
    public string StartIntersectionJson { get; set; } = string.Empty;

    [NotMapped]
    public Point StartIntersection
    {
        get => JsonSerializer.Deserialize<Point>(StartIntersectionJson) ?? new(string.Empty, 0);
        set => StartIntersectionJson = JsonSerializer.Serialize(value);
    }

    [NotMapped]
    public IEnumerable<Point> LatestIntersections
    {
        get => JsonSerializer.Deserialize<IEnumerable<Point>>(LatestIntersectionsJson) ?? [];
        set => LatestIntersectionsJson = JsonSerializer.Serialize(value);
    }

    public ulong LatestSlot => LatestIntersections.Max(x => x.Slot);
}