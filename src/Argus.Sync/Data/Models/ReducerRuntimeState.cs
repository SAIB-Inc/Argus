using System.Collections.Immutable;
using Argus.Sync.Data.Models;

namespace Argus.Sync.Data.Models;

// A custom comparer that sorts Points by Slot descending
public class DescendingSlotComparer : IComparer<Point>
{
    public int Compare(Point? x, Point? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return 1;  // Decide null ordering
        if (y is null) return -1;

        int cmp = y.Slot.CompareTo(x.Slot);
        if (cmp != 0) return cmp;

        return StringComparer.Ordinal.Compare(y.Hash, x.Hash);
    }
}

public class ReducerRuntimeState
{
    private ImmutableSortedSet<Point> _intersections =
        ImmutableSortedSet.Create<Point>(new DescendingSlotComparer());

    private int _isRollingBackInt;
    public bool IsRollingBack
    {
        get => Interlocked.CompareExchange(ref _isRollingBackInt, 0, 0) == 1;
        set => Interlocked.Exchange(ref _isRollingBackInt, value ? 1 : 0);
    }

    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public Point InitialIntersection { get; init; } = default!;
    public int RollbackBuffer { get; init; }

    public ulong CurrentSlot
    {
        get
        {
            var snapshot = Interlocked.CompareExchange(ref _intersections, _intersections, _intersections);
            return snapshot.IsEmpty ? 0UL : snapshot.First().Slot;
        }
    }

    public void AddIntersection(Point point)
    {
        // Use _intersections itself instead of null
        var oldSet = Interlocked.CompareExchange(ref _intersections, _intersections, _intersections)!;
        var newSet = oldSet.Add(point);

        while (newSet.Count > RollbackBuffer)
        {
            Point smallest = newSet.Reverse().First();
            newSet = newSet.Remove(smallest);
        }

        Interlocked.Exchange(ref _intersections, newSet);
    }

    public void RemoveIntersections(ulong fromSlot)
    {
        var oldSet = Interlocked.CompareExchange(ref _intersections, _intersections, _intersections)!;
        var newSet = oldSet.Where(e => e.Slot < fromSlot)
                           .ToImmutableSortedSet(new DescendingSlotComparer());

        Interlocked.Exchange(ref _intersections, newSet);
    }

    public Point CurrentIntersection
    {
        get
        {
            var snapshot = Interlocked.CompareExchange(ref _intersections, _intersections, _intersections)!;
            return snapshot.IsEmpty ? InitialIntersection : snapshot.First();
        }
    }

    public Point ClosestIntersection(ulong slot)
    {
        var snapshot = Interlocked.CompareExchange(ref _intersections, _intersections, _intersections)!;
        var candidate = snapshot.Where(e => e.Slot <= slot).FirstOrDefault();
        return candidate ?? InitialIntersection;
    }

    public Point StartIntersection()
    {
        var snapshot = Interlocked.CompareExchange(ref _intersections, _intersections, _intersections)!;
        var array = snapshot.ToArray();
        if (array.Length > 1)
            return array[1];
        return array.Length == 1 ? array[0] : InitialIntersection;
    }
}
