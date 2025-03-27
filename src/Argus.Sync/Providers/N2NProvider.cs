using Argus.Sync.Data.Models;

namespace Argus.Sync.Providers;

public class N2NProvider : ICardanoChainProvider
{
    public Task<Point> GetTipAsync(CancellationToken? stoppingToken = null)
    {
        throw new NotImplementedException();
    }

    IAsyncEnumerable<NextResponse> ICardanoChainProvider.StartChainSyncAsync(IEnumerable<Point> intersection, CancellationToken? stoppingToken)
    {
        throw new NotImplementedException();
    }
}