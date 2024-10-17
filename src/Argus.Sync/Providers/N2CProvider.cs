using Argus.Sync.Data.Models;
using PallasDotnet;
using PallasNextResponse = PallasDotnet.Models.NextResponse; 
namespace Argus.Sync.Providers;

public class N2CProvider(ulong NetworkMagic, string NodeSocketPath) : ICardanoChainProvider
{
    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken = null)
    {
        N2cClient client = new();
        await client.ConnectAsync(NodeSocketPath, NetworkMagic);
        await foreach (PallasNextResponse response in client.StartChainSyncAsync(
            new PallasDotnet.Models.Point(
                intersection.Slot,
                intersection.Hash
            )
        ))
        {
            if(stoppingToken.HasValue && stoppingToken.Value.IsCancellationRequested)
            {
                await client.DisconnectAsync();
                yield break;
            }
            else
            {
                switch (response.Action)
                {
                    case PallasDotnet.Models.NextResponseAction.RollForward:
                        yield return new NextResponse(
                            NextResponseAction.RollForward,
                            new Block(
                                response!.Tip.Hash,
                                response!.Tip.Slot,
                                response?.BlockCbor
                            )
                        );
                        break;
                    case PallasDotnet.Models.NextResponseAction.RollBack:
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            new Block(
                                response!.Tip.Hash,
                                response!.Tip.Slot,
                                null
                            )
                        );
                        break;
                    default:
                        continue;
                }
            }
        }
    }
}