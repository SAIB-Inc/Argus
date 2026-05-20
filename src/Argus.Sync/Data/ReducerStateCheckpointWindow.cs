using Argus.Sync.Data.Models;

namespace Argus.Sync.Data;

/// <summary>
/// Shared reducer-state checkpoint retention rules for in-memory mirrors and
/// persistent stores.
/// </summary>
internal static class ReducerStateCheckpointWindow
{
    public const int DefaultMaxCount = 10;

    public static IReadOnlyList<Point> AddRollForward(IEnumerable<Point> existing, Point point, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(point);

        return Normalize([point, .. existing.Where(p => p.Slot < point.Slot)], maxCount);
    }

    public static IReadOnlyList<Point> ApplyRollback(IEnumerable<Point> existing, ulong rollbackSlot, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(existing);

        return Normalize(existing.Where(p => p.Slot < rollbackSlot), maxCount);
    }

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
