using PallasDotnet.Models;

namespace Argus.Sync.Providers;

public class U5CProvider : ICardanoChainProvider
{
    public IAsyncEnumerable<NextResponse> StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken = null)
    {
        throw new NotImplementedException();
    }
}