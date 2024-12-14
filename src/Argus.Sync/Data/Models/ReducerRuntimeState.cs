
namespace Argus.Sync.Data.Models;

public record ReducerRuntimeState
{
    private readonly Lock _lock = new();
    private ulong _currentMax = 0;

    public string Name { get; init; } = string.Empty;
    public List<string> Dependencies { get; init; } = [];
    private List<Point> _intersections = [];
    public List<Point> Intersections
    {
        get
        {
            lock (_lock)
            {
                return _intersections;
            }
        }
        set
        {
            lock (_lock)
            {
                _intersections = value;
                _currentMax = _intersections.Any() ? _intersections.Max(e => e.Slot) : 0UL;
            }
        }
    }
    public required Point InitialIntersection { get; init; }
    public int RollbackBuffer { get; init; }
    public bool IsRollingBack { get; set; }

    public ulong CurrentSlot
    {
        get
        {
            lock (_lock)
            {
                return _currentMax;
            }
        }
    }

    public Point CurrentIntersection
    {
        get
        {
            lock (_lock)
            {
                // Check again after lock
                if (Intersections.Any())
                {
                    // CurrentSlot is also locked, but we already hold the lock, so safe
                    ulong slot = _currentMax;
                    return Intersections.First(e => e.Slot == slot);
                }

                return new Point("", 0);
            }
        }
    }

    public Point ClosestIntersection(ulong slot)
    {
        lock (_lock)
        {
            return Intersections
                .Where(e => e.Slot <= slot)
                .OrderByDescending(e => e.Slot)
                .FirstOrDefault() ?? new Point("", 0);
        }
    }

    public bool HasDependents(List<string> dependencies) => dependencies.Contains(Name);

    public void AddIntersection(Point point)
    {
        lock (_lock)
        {
            Intersections.Add(point);

            // Update the currentMax if needed
            if (point.Slot > _currentMax)
            {
                _currentMax = point.Slot;
            }

            // Maintain rollback buffer size
            if (Intersections.Count > RollbackBuffer)
            {
                Intersections = Intersections
                    .OrderByDescending(e => e.Slot)
                    .Take(RollbackBuffer)
                    .ToList();

                // Recalculate _currentMax, since we may have removed intersections
                _currentMax = Intersections.Any() ? Intersections.Max(e => e.Slot) : 0UL;
            }
        }
    }

    public void RemoveIntersections(ulong fromSlot)
    {
        lock (_lock)
        {
            Intersections.RemoveAll(e => e.Slot >= fromSlot);

            // After removal, recalculate currentMax
            _currentMax = Intersections.Any() ? Intersections.Max(e => e.Slot) : 0UL;
        }
    }

    public Point StartIntersection()
    {
        lock (_lock)
        {
            if (Intersections.Count > 1)
                return Intersections.OrderByDescending(e => e.Slot).Skip(1).First();

            return InitialIntersection;
        }
    }
}
