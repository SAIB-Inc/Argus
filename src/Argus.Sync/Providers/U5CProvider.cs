using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cardano.Models.Core.Block.Header;
using Chrysalis.Cardano.Models.Core.Block.Transaction;
using Chrysalis.Cbor;
using Utxorpc.Sdk;
using ChrysalisBlock = Chrysalis.Cardano.Models.Core.BlockEntity;

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
                        Console.WriteLine($"Deserialized block: Slot {response.AppliedBlock.NativeBytes}");
                        yield return new NextResponse(
                            NextResponseAction.RollForward,
                            null,
                            block!
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Undo:
                        blockWithEra = CborSerializer.Deserialize<BlockWithEra?>(response.UndoneBlock!.NativeBytes);
                        block = blockWithEra?.Block;
                        Console.WriteLine($"Deserialized block: Slot {response.UndoneBlock.NativeBytes}");
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            RollBackType.Inclusive,
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
                                    new VrfCert(new CborBytes([]), new CborBytes([])),
                                    new VrfCert(new CborBytes([]), new CborBytes([])),
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
                            new CborDefiniteList<TransactionBody>([]),
                            new CborDefiniteList<TransactionWitnessSet>([]),
                            new AuxiliaryDataSet([]),
                            new CborDefiniteList<CborInt>([])
                        );
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            RollBackType.Exclusive,
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
