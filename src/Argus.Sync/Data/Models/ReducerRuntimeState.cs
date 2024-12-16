namespace Argus.Sync.Data.Models;

public record ReducerRuntimeState
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly List<Point> _intersections = [];
    private long _currentSlot = 0;

    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    public Point InitialIntersection { get; init; } = default!;
    public int RollbackBuffer { get; init; }

    private volatile bool _isRollingBack;
    public bool IsRollingBack
    {
        get => _isRollingBack;
        set => _isRollingBack = value;
    }

    public ulong CurrentSlot => (ulong)Interlocked.Read(ref _currentSlot);

    public ReducerRuntimeState(IEnumerable<Point> initialPoints)
    {
        _intersections.AddRange(initialPoints);
        _currentSlot = _intersections.Any() ?
            (long)_intersections.Max(p => p.Slot) : 0L;
    }

    public Point CurrentIntersection
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                if (_intersections.Any())
                {
                    ulong slot = CurrentSlot;
                    return _intersections.First(e => e.Slot == slot);
                }
                return InitialIntersection;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public void AddIntersection(Point point)
    {
        _lock.EnterWriteLock();
        try
        {
            _intersections.Add(point);
            Interlocked.Exchange(ref _currentSlot, Math.Max(Interlocked.Read(ref _currentSlot), (long)point.Slot));

            // Maintain rollback buffer size
            if (_intersections.Count > RollbackBuffer)
            {
                _intersections.Sort((a, b) => b.Slot.CompareTo(a.Slot));
                _intersections.RemoveRange(RollbackBuffer, _intersections.Count - RollbackBuffer);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void RemoveIntersections(ulong fromSlot)
    {
        _lock.EnterWriteLock();
        try
        {
            _intersections.RemoveAll(e => e.Slot >= fromSlot);
            // After removal, recalculate currentMax
            _currentSlot = _intersections.Any() ?
                _intersections.Max(e => (long)e.Slot) : 0L;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public Point ClosestIntersection(ulong slot)
    {
        _lock.EnterReadLock();
        try
        {
            return _intersections
                .Where(e => e.Slot <= slot)
                .OrderByDescending(e => e.Slot)
                .FirstOrDefault() ?? InitialIntersection;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public Point StartIntersection()
    {
        _lock.EnterReadLock();
        try
        {
            if (_intersections.Count == 1)
                return _intersections.First();

            if (_intersections.Count > 1)
                return _intersections.OrderByDescending(e => e.Slot).Skip(1).First();

            return InitialIntersection;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
}