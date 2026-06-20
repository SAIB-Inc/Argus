using System.Runtime.CompilerServices;
using Argus.Sync.Data.Models;
using Argus.Sync.Utils;
using Chrysalis.Network.Cbor.BlockFetch;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Multiplexer;
using IBlock = Chrysalis.Codec.Types.Cardano.Core.IBlock;
using Point = Argus.Sync.Data.Models.Point;

namespace Argus.Sync.Providers;

/// <summary>
/// Cardano chain provider using the Node-to-Node (N2N) TCP protocol.
/// </summary>
/// <remarks>
/// Unlike N2C, N2N chain-sync delivers block <em>headers</em>, not full blocks. This provider
/// extracts the point (slot + hash) from each header and fetches the corresponding block bodies via
/// the BlockFetch mini-protocol, so downstream reducers receive complete <see cref="IBlock"/>
/// instances exactly as they do from <see cref="N2CProvider"/>.
/// <para>
/// Chain-sync is <b>pipelined</b>. A synchronous request→await→fetch round-trip per block makes N2N
/// latency-bound (≈4× slower than N2C). Instead the provider sends a batch of up to
/// <c>PipelineDepth</c> <c>MsgRequestNext</c> messages, drains all their header responses, then
/// fetches the whole contiguous run as a single BlockFetch range — amortizing both protocols'
/// round-trips over the batch. The depth adapts to how far the local position trails the node's tip
/// (seeded from the intersection so it is correct on the very first request) and collapses to 1 at
/// the tip so it never over-requests.
/// </para>
/// <para>
/// Each batch is drained to <b>zero outstanding chain-sync requests before any BlockFetch</b>.
/// Chain-sync and BlockFetch share one multiplexed bearer and a single demuxer; holding the
/// BlockFetch stream open while chain-sync responses accumulate would let their channel fill, block
/// the demuxer, and starve BlockFetch — a deadlock. Draining first (buffering cheap header points
/// and rollback markers, then replaying them) avoids ever holding both protocols at once.
/// </para>
/// <para>
/// Ordering is preserved losslessly: forwards collected before a rollback are fetched and yielded
/// first, then the rollback is emitted (<see cref="ArgusUtil.RollBackwardResponse"/>), then
/// collection resumes on the new fork. No block or rollback is dropped, duplicated, or reordered.
/// </para>
/// </remarks>
/// <param name="Host">The node host name or IP address.</param>
/// <param name="Port">The node's N2N TCP port (typically 3001).</param>
/// <param name="PipelineDepth">Maximum chain-sync requests per batch while catching up (default 100).</param>
public class N2NProvider(string Host, int Port, int PipelineDepth = 100) : ICardanoChainProvider, IAsyncDisposable
{
    private readonly int _maxPipelineDepth = Math.Max(1, PipelineDepth);

    private PeerClient? _sharedClient;
    private readonly SemaphoreSlim _clientSemaphore = new(1, 1);
    private ulong _connectedNetworkMagic;

    // N2N has no LocalStateQuery mini-protocol, so the tip cannot be queried directly while
    // chain sync owns the connection. Every chain-sync response carries the node's tip, so we
    // cache the latest one here and serve it from GetTipAsync. Point is a reference type, so the
    // volatile reference is safely published across the sync loop and dashboard threads.
    private volatile Point? _latestTip;

    /// <summary>An ordered chain-sync outcome buffered while draining a batch: either a contiguous run
    /// of forward header points (fetched together as one BlockFetch range) or a prepared rollback.</summary>
    private readonly record struct ChainEvent(List<SpecificPoint>? Forwards, NextResponse? Rollback);

    private async Task<PeerClient> GetOrCreateClientAsync(ulong networkMagic, CancellationToken cancellationToken)
    {
        await _clientSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Create connection if it doesn't exist or network magic changed
            if (_sharedClient == null || _connectedNetworkMagic != networkMagic)
            {
                // Dispose existing client if network magic changed
                _sharedClient?.Dispose();

                _sharedClient = await PeerClient.ConnectAsync(Host, Port, cancellationToken);
                await _sharedClient.StartAsync(networkMagic);
                _connectedNetworkMagic = networkMagic;
            }

            return _sharedClient;
        }
        finally
        {
            _ = _clientSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(IEnumerable<Point> intersection, ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        using CancellationTokenSource fallbackCts = new();
        CancellationToken token = stoppingToken ?? fallbackCts.Token;

        PeerClient client = await GetOrCreateClientAsync(networkMagic, token);

        List<SpecificPoint> cIntersections = [.. intersection.Select(p => new SpecificPoint(p.Slot, Convert.FromHexString(p.Hash)))];
        int totalIntersections = cIntersections.Count;
        bool foundIntersection = false;
        ulong intersectionSlot = 0;

        while (true)
        {
            if (cIntersections.Count == 0)
            {
                break;
            }

            cIntersections = [.. cIntersections.OrderByDescending(p => p.Slot)];
            ChainSyncMessage intersectMessage = await client.ChainSync.FindIntersectionAsync(cIntersections, token);

            if (intersectMessage is MessageIntersectFound found)
            {
                CacheTip(found.Tip);
                // Seed the catch-up distance from where we resume, so the adaptive pipeline depth is
                // correct on the very first request (deep when far behind, 1 when resuming at the tip).
                intersectionSlot = found.Point is SpecificPoint sp ? sp.Slot : 0;
                foundIntersection = true;
                break;
            }

            cIntersections = [.. cIntersections.Skip(1)];
        }

        // If no intersection was found, all saved points have been rolled back
        if (!foundIntersection)
        {
            throw new InvalidOperationException(
                $"Failed to find any valid intersection point. All {totalIntersections} saved intersection(s) have been rolled back. " +
                "The chain has rolled back beyond the saved state. Consider resetting the reducer state or increasing the rollback buffer size.");
        }

        // ---- Pipelined chain-sync loop ----
        // Each iteration sends a batch of `target` MsgRequestNext, drains ALL of their terminal
        // responses (so zero chain-sync requests stay outstanding), then fetches the collected header
        // run as one BlockFetch range. Draining to zero first is what makes it deadlock-free: the two
        // mini-protocols share one demuxer, so we must never hold the BlockFetch stream open while
        // chain-sync responses are still arriving. MessageAwaitReply is non-terminal (the tip has been
        // reached for that request); we keep draining until every request resolves, which at the tip
        // simply follows new blocks. Adaptive depth shrinks the batch toward 1 near the tip, so the
        // drain never blocks for long.
        ulong lastHeaderSlot = intersectionSlot;
        ulong tipSlot = TipSlotOrZero(_latestTip);

        while (!token.IsCancellationRequested)
        {
            ulong tipGap = tipSlot > lastHeaderSlot ? tipSlot - lastHeaderSlot : 0;
            int target = AdaptivePipelineDepth(_maxPipelineDepth, tipGap);

            await client.ChainSync.SendNextRequestBatchAsync(target, token);
            int inFlight = target;

            // Drain the whole batch, buffering ordered events (cheap header points / rollback markers).
            List<ChainEvent> events = [];
            List<SpecificPoint> run = [];

            while (inFlight > 0 && !token.IsCancellationRequested)
            {
                MessageNextResponse response = await client.ChainSync.ReceiveNextResponseAsync(token);
                switch (response)
                {
                    case N2NMessageRollForward forward:
                        inFlight--;
                        CacheTip(forward.Tip);
                        tipSlot = TipSlotOrZero(_latestTip);
                        SpecificPoint? headerPoint = TryExtractHeaderPoint(forward.Payload);
                        if (headerPoint is not null)
                        {
                            run.Add(headerPoint);
                            lastHeaderSlot = headerPoint.Slot;
                        }
                        break;

                    case MessageRollBackward rollback:
                        inFlight--;
                        // Close the contiguous forward run before the rollback so it is fetched and
                        // yielded first; collection then resumes on the new fork after it.
                        if (run.Count > 0)
                        {
                            events.Add(new ChainEvent(run, null));
                            run = [];
                        }
                        CacheTip(rollback.Tip);
                        tipSlot = TipSlotOrZero(_latestTip);
                        events.Add(new ChainEvent(null, ArgusUtil.RollBackwardResponse(rollback.Point)));
                        break;

                    default:
                        // MessageAwaitReply: non-terminal. The request stays outstanding and resolves
                        // when the chain advances, so keep draining (this is normal tip-following).
                        break;
                }
            }

            if (run.Count > 0)
            {
                events.Add(new ChainEvent(run, null));
            }

            // Batch fully drained (inFlight == 0) — safe to BlockFetch. Replay events in chain order.
            foreach (ChainEvent chainEvent in events)
            {
                if (chainEvent.Forwards is { } forwards)
                {
                    await foreach (NextResponse forwarded in DrainForwardsAsync(client, forwards, token))
                    {
                        yield return forwarded;
                    }
                }
                else if (chainEvent.Rollback is { } rollbackResponse)
                {
                    yield return rollbackResponse;
                }
            }
        }
    }

    /// <summary>
    /// Fetches the bodies for a contiguous run of header points as one BlockFetch range and yields each
    /// as a roll-forward. The run is consecutive chain blocks (collected from successive chain-sync
    /// roll-forwards), so the inclusive range returns exactly those blocks, in order.
    /// </summary>
    private static async IAsyncEnumerable<NextResponse> DrainForwardsAsync(PeerClient client, List<SpecificPoint> points, [EnumeratorCancellation] CancellationToken token)
    {
        if (points.Count == 0)
        {
            yield break;
        }

        await client.BlockFetch.RequestRangeAsync(points[0], points[^1], token);
        await foreach (BlockFetchMessage message in client.BlockFetch.ReceiveBlockMessagesAsync(token))
        {
            if (message is BlockBody body)
            {
                // Same era-aware, defensively-copied deserialization the N2C path uses. A body that
                // fails to deserialize is anomalous (corrupt/unknown era) — surface it rather than
                // silently dropping a block, which would desync the indexed chain.
                IBlock block = ArgusUtil.DeserializeBlockWithEra(body.Body.Value)
                    ?? throw new InvalidOperationException("BlockFetch returned a body that could not be deserialized.");
                yield return new NextResponse(NextResponseAction.RollForward, null, block);
            }
        }
    }

    /// <summary>
    /// Decodes an N2N RollForward header payload ([era, header]) to its chain point, or null if the
    /// header cannot be parsed (the caller skips it rather than aborting the stream).
    /// </summary>
    private static SpecificPoint? TryExtractHeaderPoint(N2NBlockHeader headerPayload)
    {
        try
        {
            ChainSyncHeader header = ChainSyncHeader.Decode(headerPayload.Raw);
            ChainPoint chainPoint = header.ExtractPoint();
            return new SpecificPoint(chainPoint.Slot, chainPoint.Hash);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Pipeline depth as a function of the gap (in slots) to the node's tip.</summary>
    private static int AdaptivePipelineDepth(int maxDepth, ulong tipGap)
    {
        int target = tipGap switch
        {
            0 => 1,
            <= 4 => 1,
            <= 20 => 2,
            <= 100 => 5,
            <= 500 => 20,
            <= 2_000 => 100,
            <= 10_000 => 500,
            <= 50_000 => 2_000,
            _ => maxDepth
        };

        return Math.Min(maxDepth, Math.Max(1, target));
    }

    private static ulong TipSlotOrZero(Point? tip) => tip?.Slot ?? 0;

    /// <inheritdoc />
    public async Task<Point> GetTipAsync(ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        // Prefer the tip cached from the live chain-sync stream so we never touch the busy sync
        // channel concurrently. Before the first response arrives, fall back to a short-lived,
        // independent query connection.
        Point? cached = _latestTip;
        if (cached is not null)
        {
            return cached;
        }

        using CancellationTokenSource fallbackCts = new();
        CancellationToken token = stoppingToken ?? fallbackCts.Token;

        using PeerClient probe = await PeerClient.ConnectAsync(Host, Port, token);
        await probe.StartAsync(networkMagic);

        ChainSyncMessage response = await probe.ChainSync.FindIntersectionAsync([new OriginPoint()], token);
        Tip tip = response switch
        {
            MessageIntersectFound found => found.Tip,
            MessageIntersectNotFound notFound => notFound.Tip,
            _ => throw new InvalidOperationException("Unexpected response to FindIntersection while querying the N2N tip.")
        };

        if (tip.Slot is not SpecificPoint tipPoint)
        {
            throw new InvalidOperationException("Node reported the origin as its tip; no specific tip point is available.");
        }

        Point result = new(Convert.ToHexString(tipPoint.Hash.Span).ToUpperInvariant(), tipPoint.Slot);
        _latestTip = result;
        return result;
    }

    private void CacheTip(Tip tip)
    {
        if (tip?.Slot is SpecificPoint tipPoint)
        {
            _latestTip = new Point(Convert.ToHexString(tipPoint.Hash.Span).ToUpperInvariant(), tipPoint.Slot);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _clientSemaphore.WaitAsync();
        try
        {
            _sharedClient?.Dispose();
            _sharedClient = null;
        }
        finally
        {
            _ = _clientSemaphore.Release();
            _clientSemaphore.Dispose();
        }
    }
}
