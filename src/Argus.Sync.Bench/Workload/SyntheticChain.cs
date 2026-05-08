namespace Argus.Sync.Bench.Workload;

/// <summary>
/// Pre-generates a fixed list of synthetic envelopes. Used to isolate pipeline
/// overhead from chain I/O — bench runs iterate over the same in-memory list
/// for every implementation, so wall-clock differences come from the pipeline
/// only, not from chain producer variance.
/// </summary>
public static class SyntheticChain
{
    public static IReadOnlyList<Envelope> Generate(int blockCount, int payloadBytes = 3072, int rollbackEveryNthBlock = 0)
    {
        Random rng = new(Seed: 42);
        List<Envelope> list = new(blockCount);
        ulong startSlot = 80_000_000UL;

        for (int i = 0; i < blockCount; i++)
        {
            byte[] payload = new byte[payloadBytes];
            rng.NextBytes(payload);

            EnvelopeAction action = (rollbackEveryNthBlock > 0 && i > 0 && i % rollbackEveryNthBlock == 0)
                ? EnvelopeAction.RollBackward
                : EnvelopeAction.RollForward;

            list.Add(new Envelope(
                Slot: startSlot + (ulong)(i * 20),
                Height: (ulong)(3_000_000 + i),
                Action: action,
                PayloadBytes: payload
            ));
        }

        return list;
    }
}
