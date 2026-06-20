namespace Argus.Sync.Workers;

/// <summary>
/// Cross-process guard that ensures only one Argus indexer is active against a given
/// database at a time. The indexer awaits <see cref="WaitForAcquisitionAsync"/> before it
/// begins processing; a second instance (e.g. a redeploy overlapping the old one) parks
/// there until the first instance releases the lock.
/// </summary>
public interface ISingleInstanceLock
{
    /// <summary>
    /// Completes once this process holds the single-instance lock. If the host shuts down
    /// before acquisition the returned task is cancelled; if acquisition faults it is faulted.
    /// </summary>
    /// <param name="ct">Cancels the wait (typically the worker's stopping token).</param>
    Task WaitForAcquisitionAsync(CancellationToken ct);
}
