using Argus.Sync.Data.Models;

namespace Argus.Sync.Providers;

public class N2NProvider : ICardanoChainProvider
{

    IAsyncEnumerable<NextResponse> ICardanoChainProvider.StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken)
    {
        throw new NotImplementedException();
    }
}