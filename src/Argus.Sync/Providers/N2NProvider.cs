using PallasDotnet.Models;

namespace Argus.Sync.Providers;

public class N2NProvider : ICardanoChainProvider
{
    public IAsyncEnumerable<NextResponse> StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken = null)
    {
        throw new NotImplementedException();
    }
}