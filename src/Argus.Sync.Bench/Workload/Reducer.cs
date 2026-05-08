namespace Argus.Sync.Bench.Workload;

/// <summary>
/// Synthetic reducer used by the bench. Real reducers do CBOR deserialization,
/// EF entity tracking, and DB writes; the bench abstracts all of that into a
/// per-envelope async work delegate so we can measure pipeline overhead under
/// different cost models (CPU-light, DB-realistic, bulk-write).
/// </summary>
public sealed class BenchReducer(string name, WorkProfile profile)
{
    private long _processed;

    public string Name { get; } = name;
    public WorkProfile Profile { get; } = profile;
    public long ProcessedCount => Interlocked.Read(ref _processed);
    public ulong LatestSlot { get; private set; }

    public async ValueTask ProcessAsync(Envelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        switch (Profile)
        {
            case WorkProfile.CpuLight:
                await Task.Yield();
                break;
            case WorkProfile.DbRealistic:
                await Task.Delay(TimeSpan.FromMilliseconds(3), ct).ConfigureAwait(false);
                break;
            case WorkProfile.CpuHeavy:
                Thread.SpinWait(50_000);
                _ = new byte[1024];
                break;
            case WorkProfile.BulkWrite:
                if (Interlocked.Read(ref _processed) % 100 == 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), ct).ConfigureAwait(false);
                }
                break;
            default:
                throw new InvalidOperationException($"Unknown work profile: {Profile}");
        }

        LatestSlot = envelope.Slot;
        _ = Interlocked.Increment(ref _processed);
    }
}

public enum WorkProfile
{
    CpuLight,
    DbRealistic,
    CpuHeavy,
    BulkWrite,
}
