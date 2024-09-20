using PallasDotnet;
using PallasDotnet.Models;

namespace Argus.Sync.Providers;

public class N2CProvider(ulong NetworkMagic, string NodeSocketPath) : ICardanoChainProvider
{
    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken = null)
    {
        N2cClient client = new();
        await client.ConnectAsync(NodeSocketPath, NetworkMagic);
        await foreach (NextResponse response in client.StartChainSyncAsync(intersection))
        {
            if(stoppingToken.HasValue && stoppingToken.Value.IsCancellationRequested)
            {
                await client.DisconnectAsync();
                yield break;
            }
            else
            {
                yield return response;
            }
        }
    }
}