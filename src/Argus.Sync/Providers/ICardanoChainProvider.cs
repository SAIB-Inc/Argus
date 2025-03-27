using Argus.Sync.Data.Models;
using Chrysalis.Network.Cbor.Common;
using Point = Argus.Sync.Data.Models.Point;
namespace Argus.Sync.Providers;

public interface ICardanoChainProvider
{
    IAsyncEnumerable<NextResponse> StartChainSyncAsync(IEnumerable<Point> intersection, CancellationToken? stoppingToken = null);
    Task<Point> GetTipAsync(CancellationToken? stoppingToken = null);
}