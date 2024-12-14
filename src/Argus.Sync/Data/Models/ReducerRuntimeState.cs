namespace Argus.Sync.Data.Models;

public record ReducerRuntimeState
{
    public string Name { get; init; } = string.Empty;
    public List<string> Dependencies { get; init; } = [];
    public List<Point> Intersections { get; set; } = [];
    public int RollbackBuffer { get; init; } = 10;

    public ulong CurrentSlot => Intersections.Any()
        ? Intersections.Max(e => e.Slot)
        : 0UL;

    public Point CurrentIntersection => Intersections.Any()
        ? Intersections.First(e => e.Slot == CurrentSlot)
        : new Point("", 0);

    public Point ClosestIntersection(ulong slot) => Intersections
        .Where(e => e.Slot <= slot)
        .OrderByDescending(e => e.Slot)
        .FirstOrDefault() ?? new Point("", 0);

    public bool HasDependents(List<string> dependencies) => dependencies.Contains(Name);

    public void AddIntersection(Point point)
    {
        Intersections.Add(point);
        if (Intersections.Count > RollbackBuffer)
        {
            Intersections = Intersections.OrderByDescending(e => e.Slot).Take(RollbackBuffer).ToList();
        }
    }

    public void RemoveIntersections(ulong fromSlot)
    {
        Intersections.RemoveAll(e => e.Slot >= fromSlot);
    }
}
