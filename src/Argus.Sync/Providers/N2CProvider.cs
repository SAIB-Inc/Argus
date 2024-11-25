using Argus.Sync.Data.Models;
using Chrysalis.Cbor;
using PallasDotnet;
using PallasNextResponse = PallasDotnet.Models.NextResponse;
using ChrysalisBlock = Chrysalis.Cardano.Core.Block;

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
                        ChrysalisBlock? block = CborSerializer.Deserialize<ChrysalisBlock>(response.BlockCbor);
                        yield return new NextResponse(
                            NextResponseAction.RollForward,
                            null,
                             block!
                        );
                        break;
                   case PallasDotnet.Models.NextResponseAction.RollBack:
                        block = CborSerializer.Deserialize<ChrysalisBlock>(response.BlockCbor);
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            RollBackType.Exclusive,
                            block!
                        );
                        break;
                    default:
                        continue;
                }
            }
        }
    }
}