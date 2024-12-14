using Argus.Sync.Data.Models;
using Chrysalis.Cbor;
using PallasNextResponse = Pallas.NET.Models.NextResponse;
using ChrysalisBlock = Chrysalis.Cardano.Core.Types.Block.Block;
using Pallas.NET;
using Pallas.NET.Models.Enums;
using Chrysalis.Cbor.Converters;

namespace Argus.Sync.Providers;

public class N2CProvider(ulong NetworkMagic, string NodeSocketPath) : ICardanoChainProvider
{
    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken = null)
    {
        Client client = new();
        await client.ConnectAsync(NodeSocketPath, NetworkMagic, ClientType.N2C);
        await foreach (PallasNextResponse response in client.StartChainSyncAsync(
            [
                new(intersection.Slot, intersection.Hash)
            ]
        ))
        {
            if (stoppingToken.HasValue && stoppingToken.Value.IsCancellationRequested)
            {
                await client.DisconnectAsync();
                yield break;
            }
            else
            {
                switch (response.Action)
                {
                    case Pallas.NET.Models.Enums.NextResponseAction.RollForward:
                        ChrysalisBlock? block = CborSerializer.Deserialize<ChrysalisBlock>(response.BlockCbor);
                        yield return new NextResponse(
                            Data.Models.NextResponseAction.RollForward,
                            null,
                             block!
                        );
                        break;
                    case Pallas.NET.Models.Enums.NextResponseAction.RollBack:
                        block = CborSerializer.Deserialize<ChrysalisBlock>(response.BlockCbor);
                        yield return new NextResponse(
                            Data.Models.NextResponseAction.RollBack,
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