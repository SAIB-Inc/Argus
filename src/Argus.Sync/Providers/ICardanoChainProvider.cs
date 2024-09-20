using PallasDotnet.Models;

namespace Argus.Sync.Providers;

public interface ICardanoChainProvider
{
    IAsyncEnumerable<NextResponse> StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken = null);
}