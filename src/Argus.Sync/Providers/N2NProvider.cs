namespace Argus.Sync.Providers;

public class N2NProvider : ICardanoChainProvider
{
    public IAsyncEnumerable<Data.Models.NextResponse> StartChainSyncAsync(Data.Models.Point intersection, CancellationToken? stoppingToken = null)
    {
        throw new NotImplementedException();
    }
}