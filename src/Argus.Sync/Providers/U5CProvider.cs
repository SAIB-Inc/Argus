using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cbor;
using Utxorpc.Sdk;
using ChrysalisBlock = Chrysalis.Cardano.Models.Core.Block.Block;

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
                        BlockWithEra? blockWithEra = CborSerializer.Deserialize<BlockWithEra?>(response.AppliedBlock!.NativeBytes);
                        ChrysalisBlock? block = blockWithEra?.Block;
                        yield return new NextResponse(
                            NextResponseAction.RollForward,
                            block!
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Undo:
                        blockWithEra = CborSerializer.Deserialize<BlockWithEra?>(response.UndoneBlock!.NativeBytes);
                        block = blockWithEra?.Block;
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            block!
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Reset:
                        
                        block = new ChrysalisBlock(
                            new BlockHeader(
                                new AlonzoHeaderBody(
                                    new CborUlong(0),
                                    new CborUlong(response.ResetRef!.Index),
                                    new CborNullable<CborBytes>(new CborBytes([])),
                                    new CborBytes([]),
                                    new CborBytes([]),
                                    new VrfCert(new CborBytes([]),new CborBytes([])),
                                    new VrfCert(new CborBytes([]),new CborBytes([])),
                                    new CborUlong(0),
                                    new CborBytes([]),
                                    new CborBytes([]),
                                    new CborUlong(0),
                                    new CborUlong(0),
                                    new CborBytes([]),
                                    new CborUlong(0),
                                    new CborUlong(0)
                                ),
                            new CborBytes([])
                            ),
                            null,
                            null,
                            null,
                            null
                        );
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            block
                        );
                        break;
                    default:
                        continue;
                }

            }
        }
    }
}