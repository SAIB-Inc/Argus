using Argus.Sync.Data.Models;
using Argus.Sync.Utils;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Point = Argus.Sync.Data.Models.Point;

namespace Argus.Sync.Providers;

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
            _clientSemaphore.Release();
        }
    }

    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(IEnumerable<Point> intersections, ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        stoppingToken ??= new CancellationTokenSource().Token;

        NodeClient client = await GetOrCreateClientAsync(networkMagic, stoppingToken.Value);

        IEnumerable<Chrysalis.Network.Cbor.Common.Point> cIntersections = intersections.Select(p => (Chrysalis.Network.Cbor.Common.Point)new SpecificPoint(p.Slot, Convert.FromHexString(p.Hash)));
        int totalIntersections = cIntersections.Count();
        bool foundIntersection = false;

        while (true)
        {
            if (!cIntersections.Any())
            {
                break;
            }

            cIntersections = cIntersections.OrderByDescending(p => p is SpecificPoint sp ? sp.Slot : 0UL);
            ChainSyncMessage intersectMessage = await client.ChainSync!.FindIntersectionAsync(cIntersections, stoppingToken.Value);

            if (intersectMessage is MessageIntersectFound found)
            {
                foundIntersection = true;
                break;
            }

            cIntersections = cIntersections.Skip(1);
        }

        // If no intersection was found, all saved points have been rolled back
        if (!foundIntersection)
        {
            throw new InvalidOperationException(
                $"Failed to find any valid intersection point. All {totalIntersections} saved intersection(s) have been rolled back. " +
                "The chain has rolled back beyond the saved state. Consider resetting the reducer state or increasing the rollback buffer size.");
        }

        while (!stoppingToken.Value.IsCancellationRequested)
        {
            MessageNextResponse? nextResponse = await client.ChainSync!.NextRequestAsync(stoppingToken.Value);
            switch (nextResponse)
            {
                case MessageRollBackward msg:
                    SpecificPoint rollbackPoint = (SpecificPoint)msg.Point;
                    yield return new NextResponse(
                          NextResponseAction.RollBack,
                          RollBackType.Exclusive,
                          null,
                          rollbackPoint.Slot
                      );
                    break;
                case MessageRollForward msg:
                    var block = ArgusUtil.DeserializeBlockWithEra(msg.Payload.Value);
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

    public async Task<Point> GetTipAsync(ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        stoppingToken ??= new CancellationTokenSource().Token;

        try
        {
            NodeClient client = await GetOrCreateClientAsync(networkMagic, stoppingToken.Value);
            Tip tip = await client.LocalStateQuery!.GetTipAsync();
            SpecificPoint tipPoint = (SpecificPoint)tip.Slot;
            return new(Convert.ToHexString(tipPoint.Hash.Span).ToLowerInvariant(), tipPoint.Slot);
        }
        catch
        {
            // If connection fails, dispose shared client so it gets recreated on next call
            await _clientSemaphore.WaitAsync(stoppingToken.Value);
            try
            {
                _sharedClient?.Dispose();
                _sharedClient = null;
            }
            finally
            {
                _clientSemaphore.Release();
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _clientSemaphore.WaitAsync();
        try
        {
            _sharedClient?.Dispose();

            _sharedClient = null;
        }
        finally
        {
            _clientSemaphore.Release();
            _clientSemaphore.Dispose();
        }
    }
}
