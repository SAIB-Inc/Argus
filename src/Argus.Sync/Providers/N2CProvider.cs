using Argus.Sync.Data.Models;
using Argus.Sync.Extensions.Chrysalis;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cbor;
using PallasDotnet;
using PallasNextResponse = PallasDotnet.Models.NextResponse;
using ChrysalisBlock = Chrysalis.Cardano.Models.Core.Block.Block;
using Argus.Sync.Utils;

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
                        BlockWithEra? blockWithEra = CborSerializer.Deserialize<BlockWithEra>(response.BlockCbor);
                        ulong slot = blockWithEra!.Block.Slot();
                        byte[] header = CborSerializer.Serialize(blockWithEra.Block.Header);
                        byte[] blockHash = header.ToBlake2b();
                        yield return new NextResponse(
                            NextResponseAction.RollForward,
                            new Data.Models.Block(
                                Convert.ToHexString(blockHash),
                                slot,
                                response?.BlockCbor
                            )
                        );
                        break;
                    case PallasDotnet.Models.NextResponseAction.RollBack:
                        ChrysalisBlock? block = CborSerializer.Deserialize<ChrysalisBlock>(response.BlockCbor);
                        slot = block!.Slot();
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            new Data.Models.Block(
                                null,
                                slot,
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