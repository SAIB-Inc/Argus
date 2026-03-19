using Argus.Sync.Data.Models;
using Argus.Sync.Utils;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using IBlock = Chrysalis.Codec.Types.Cardano.Core.IBlock;
using Point = Argus.Sync.Data.Models.Point;

namespace Argus.Sync.Providers;

/// <summary>
/// Cardano chain provider using the Node-to-Client (N2C) Unix socket protocol.
/// </summary>
/// <param name="NodeSocketPath">The file path to the Cardano node Unix socket.</param>
public class N2CProvider(string NodeSocketPath) : ICardanoChainProvider, IAsyncDisposable
{
    private NodeClient? _sharedClient;
    private readonly SemaphoreSlim _clientSemaphore = new(1, 1);
    private ulong _connectedNetworkMagic;

    private async Task<NodeClient> GetOrCreateClientAsync(ulong networkMagic, CancellationToken cancellationToken)
    {
        await _clientSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Create connection if it doesn't exist or network magic changed
            if (_sharedClient == null || _connectedNetworkMagic != networkMagic)
            {
                // Dispose existing client if network magic changed
                _sharedClient?.Dispose();

                _sharedClient = await NodeClient.ConnectAsync(NodeSocketPath, cancellationToken);
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

        NodeClient client = await GetOrCreateClientAsync(networkMagic, token);

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
            ChainSyncMessage intersectMessage = await client.ChainSync!.FindIntersectionAsync(cIntersections, token);

            if (intersectMessage is MessageIntersectFound)
            {
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
            MessageNextResponse? nextResponse = await client.ChainSync!.NextRequestAsync(token);
            switch (nextResponse)
            {
                case MessageRollBackward msg when msg.Point is SpecificPoint rbPoint:
                    yield return new NextResponse(
                          NextResponseAction.RollBack,
                          RollBackType.Exclusive,
                          null,
                          rbPoint.Slot
                      );
                    break;
                case MessageRollForward msg:
                    IBlock? block = ArgusUtil.DeserializeBlockWithEra(msg.Payload.Value);
                    yield return new NextResponse(
                          NextResponseAction.RollForward,
                          null,
                          block!
                      );
                    break;
                default:
                    continue;
            }
        }
    }

    /// <inheritdoc />
    public async Task<Point> GetTipAsync(ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        using CancellationTokenSource fallbackCts = new();
        CancellationToken token = stoppingToken ?? fallbackCts.Token;

        try
        {
            NodeClient client = await GetOrCreateClientAsync(networkMagic, token);
            Tip tip = await client.LocalStateQuery!.GetTipAsync();
            SpecificPoint tipPoint = (SpecificPoint)tip.Slot;
            return new(Convert.ToHexString(tipPoint.Hash.Span).ToUpperInvariant(), tipPoint.Slot);
        }
        catch
        {
            // If connection fails, dispose shared client so it gets recreated on next call
            await _clientSemaphore.WaitAsync(token);
            try
            {
                _sharedClient?.Dispose();
                _sharedClient = null;
            }
            finally
            {
                _ = _clientSemaphore.Release();
            }
            throw;
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
