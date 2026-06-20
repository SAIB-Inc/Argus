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
/// therefore extracts the point (slot + hash) from each header and fetches the corresponding
/// block body via the BlockFetch mini-protocol, so downstream reducers receive complete
/// <see cref="IBlock"/> instances exactly as they do from <see cref="N2CProvider"/>. Rollbacks
/// arrive through chain-sync identically to N2C and are mapped by <see cref="ArgusUtil.RollBackwardResponse"/>.
/// </remarks>
/// <param name="Host">The node host name or IP address.</param>
/// <param name="Port">The node's N2N TCP port (typically 3001).</param>
public class N2NProvider(string Host, int Port) : ICardanoChainProvider, IAsyncDisposable
{
    private PeerClient? _sharedClient;
    private readonly SemaphoreSlim _clientSemaphore = new(1, 1);
    private ulong _connectedNetworkMagic;

    // N2N has no LocalStateQuery mini-protocol, so the tip cannot be queried directly while
    // chain sync owns the connection. Every chain-sync response carries the node's tip, so we
    // cache the latest one here and serve it from GetTipAsync. Point is a reference type, so the
    // volatile reference is safely published across the sync loop and dashboard threads.
    private volatile Point? _latestTip;

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

        while (!token.IsCancellationRequested)
        {
            MessageNextResponse? nextResponse = await client.ChainSync.NextRequestAsync(token);
            switch (nextResponse)
            {
                case MessageRollBackward msg:
                    // Handles both SpecificPoint (Exclusive) and OriginPoint (Inclusive/0).
                    CacheTip(msg.Tip);
                    yield return ArgusUtil.RollBackwardResponse(msg.Point);
                    break;
                case N2NMessageRollForward msg:
                    // N2N delivers only the header; fetch the body before forwarding a full block.
                    CacheTip(msg.Tip);
                    IBlock block = await FetchBlockAsync(client, msg.Payload, token);
                    yield return new NextResponse(
                          NextResponseAction.RollForward,
                          null,
                          block
                      );
                    break;
                default:
                    // MessageAwaitReply (at tip): the next NextRequestAsync blocks until the
                    // server pushes the next block, so this never busy-loops.
                    continue;
            }
        }
    }

    /// <summary>
    /// Resolves a header delivered by N2N chain-sync to a full block by extracting its point and
    /// fetching the body over the BlockFetch mini-protocol.
    /// </summary>
    private static async Task<IBlock> FetchBlockAsync(PeerClient client, N2NBlockHeader headerPayload, CancellationToken token)
    {
        // N2N RollForward carries [era, header]; decode era-aware (Byron through Conway) to extract its point.
        ChainSyncHeader header = ChainSyncHeader.Decode(headerPayload.Raw);
        ChainPoint chainPoint = header.ExtractPoint();
        SpecificPoint fetchPoint = new(chainPoint.Slot, chainPoint.Hash);

        await client.BlockFetch.RequestRangeAsync(fetchPoint, fetchPoint, token);

        IBlock? block = null;
        await foreach (BlockFetchMessage message in client.BlockFetch.ReceiveBlockMessagesAsync(token))
        {
            if (message is BlockBody body)
            {
                // Same era-aware, defensively-copied deserialization the N2C path uses.
                block = ArgusUtil.DeserializeBlockWithEra(body.Body.Value);
            }
        }

        return block ?? throw new InvalidOperationException(
            $"BlockFetch returned no block body for the header at slot {chainPoint.Slot} (hash {header.Hash()}).");
    }

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
