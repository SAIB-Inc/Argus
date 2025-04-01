using Argus.Sync.Data.Models;

namespace Argus.Sync.Providers;

public class N2NProvider : ICardanoChainProvider
{
    public Task<Point> GetTipAsync(ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        throw new NotImplementedException();
    }

    IAsyncEnumerable<NextResponse> ICardanoChainProvider.StartChainSyncAsync(IEnumerable<Point> intersection, ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        throw new NotImplementedException();
    }
}