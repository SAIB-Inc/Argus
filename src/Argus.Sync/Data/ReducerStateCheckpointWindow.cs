using Argus.Sync.Data.Models;

namespace Argus.Sync.Data;

/// <summary>
/// Shared reducer-state checkpoint retention rules for in-memory mirrors and
/// persistent stores. Public because every storage backend's unit of work needs the
/// same rolling-window math when it writes a reducer's checkpoint.
/// </summary>
public static class ReducerStateCheckpointWindow
{
    /// <summary>Default number of recent checkpoints retained in the rolling window.</summary>
    public const int DefaultMaxCount = 10;

    /// <summary>
    /// Adds a roll-forward checkpoint, dropping any existing points at or after its slot, then caps
    /// the window to <paramref name="maxCount"/> newest points.
    /// </summary>
    public static IReadOnlyList<Point> AddRollForward(IEnumerable<Point> existing, Point point, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(point);

        return Normalize([point, .. existing.Where(p => p.Slot < point.Slot)], maxCount);
    }

    /// <summary>Rewinds the window to checkpoints strictly before <paramref name="rollbackSlot"/>, capped to <paramref name="maxCount"/>.</summary>
    public static IReadOnlyList<Point> ApplyRollback(IEnumerable<Point> existing, ulong rollbackSlot, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(existing);

        return Normalize(existing.Where(p => p.Slot < rollbackSlot), maxCount);
    }

    /// <summary>Orders points newest-first, de-duplicates by slot, and keeps the newest <paramref name="maxCount"/>.</summary>
    public static IReadOnlyList<Point> Normalize(IEnumerable<Point> existing, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(existing);

        int cappedMaxCount = Math.Max(1, maxCount);
        return [.. existing
            .OrderByDescending(p => p.Slot)
            .DistinctBy(p => p.Slot)
            .Take(cappedMaxCount)];
    }
}
