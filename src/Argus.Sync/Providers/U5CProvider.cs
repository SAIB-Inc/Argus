using Argus.Sync.Data.Models;
using Utxorpc.Sdk;
using ChrysalisBlock = Chrysalis.Cardano.Core.Types.Block.Block;
using CborBytes = Chrysalis.Cbor.Types.Primitives.CborBytes;
using U5CNextResponse = Utxorpc.Sdk.Models.NextResponse;
using Chrysalis.Cardano.Core.Types.Block;
using Chrysalis.Cbor.Converters;
using Chrysalis.Cardano.Core.Types.Block.Header;
using Chrysalis.Cardano.Core.Types.Block.Header.Body;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
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
                        BlockWithEra? blockWithEra = CborSerializer.Deserialize<BlockWithEra>(response.AppliedBlock!.NativeBytes);
                        ChrysalisBlock? block = blockWithEra?.Block;
                        yield return new NextResponse(
                            NextResponseAction.RollForward,
                            null,
                            block!
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Undo:
                        blockWithEra = CborSerializer.Deserialize<BlockWithEra>(response.UndoneBlock!.NativeBytes);
                        block = blockWithEra?.Block;
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            RollBackType.Inclusive,
                            block!
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Reset:
                        block = new AlonzoBlock(
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
                            new CborDefList<TransactionBody>([]),
                            new CborDefList<TransactionWitnessSet>([]),
                            new AuxiliaryDataSet([]),
                            new CborDefList<CborInt>([])
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
