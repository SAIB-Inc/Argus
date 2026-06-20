namespace Argus.Sync.Bench.Workload;

/// <summary>
/// Synthetic block envelope used by the bench. Mirrors the shape of the real
/// NextResponse the worker pumps through reducers, with only the fields that
/// affect pipeline behavior. The PayloadBytes field exists so we can size
/// envelopes realistically (~3 KB typical block, ~30 KB peak) and observe
/// memory ceilings under bounded-channel backpressure.
/// </summary>
public sealed record Envelope(
    ulong Slot,
    ulong Height,
    EnvelopeAction Action,
    byte[] PayloadBytes
);

public enum EnvelopeAction
{
    RollForward,
    RollBackward,
}
