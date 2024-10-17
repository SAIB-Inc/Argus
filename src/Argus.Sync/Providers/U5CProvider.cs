using Argus.Sync.Data.Models;
using Utxorpc.Sdk;

using U5CNextResponse = Utxorpc.Sdk.Models.NextResponse; 
namespace Argus.Sync.Providers;

public class U5CProvider(string url, Dictionary<string, string> header) : ICardanoChainProvider
{
    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken = null)
    {
        
        var client = new SyncServiceClient(
            url,
            header
        );
        await foreach (U5CNextResponse? response in client.FollowTipAsync(
            new Utxorpc.Sdk.Models.BlockRef
            (
                intersection.Hash,
                intersection.Slot
            )))
        {
            
            if (stoppingToken.HasValue && stoppingToken.Value.IsCancellationRequested)
            {
                yield break;
            }
            else
            {
                switch (response.Action)
                {
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Apply:
                        yield return new NextResponse(
                            NextResponseAction.RollForward,
                            new Block(
                                response!.AppliedBlock!.Hash,
                                response!.AppliedBlock!.Slot,
                                response?.AppliedBlock.NativeBytes
                            )
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Undo:
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            new Block(
                                response!.UndoneBlock!.Hash,
                                response!.UndoneBlock!.Slot,
                                null
                            )
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Reset:
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            new Block(
                                response!.ResetRef!.Hash,
                                response!.ResetRef!.Index,
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