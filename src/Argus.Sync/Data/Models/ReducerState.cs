using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Argus.Sync.Data.Models;

/// <summary>
/// Represents the persisted state of a reducer, including its latest intersection points and start intersection.
/// </summary>
/// <param name="Name">The unique name identifying this reducer.</param>
/// <param name="CreatedAt">The timestamp when this reducer state was created.</param>
public record ReducerState(string Name, DateTimeOffset CreatedAt)
{
    /// <summary>Gets or sets the unique name identifying this reducer.</summary>
    public string Name { get; set; } = Name;
    /// <summary>Gets or sets the timestamp when this reducer state was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = CreatedAt;
    /// <summary>Gets or sets the JSON-serialized latest intersection points.</summary>
    public string LatestIntersectionsJson { get; set; } = string.Empty;
    /// <summary>Gets or sets the JSON-serialized start intersection point.</summary>
    public string StartIntersectionJson { get; set; } = string.Empty;

    /// <summary>Gets or sets the start intersection point, deserialized from JSON.</summary>
    [NotMapped]
    public Point StartIntersection
    {
        get => JsonSerializer.Deserialize<Point>(StartIntersectionJson) ?? new(string.Empty, 0);
        set => StartIntersectionJson = JsonSerializer.Serialize(value);
    }

    /// <summary>Gets or sets the latest intersection points, deserialized from JSON.</summary>
    [NotMapped]
    public IEnumerable<Point> LatestIntersections
    {
        get => JsonSerializer.Deserialize<IEnumerable<Point>>(LatestIntersectionsJson) ?? [];
        set => LatestIntersectionsJson = JsonSerializer.Serialize(value);
    }

    /// <summary>Gets the latest slot number from the intersection points.</summary>
    public ulong LatestSlot => LatestIntersections.Max(x => x.Slot);
}
