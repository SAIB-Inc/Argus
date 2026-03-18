using Argus.Sync.Data.Models;
using Utxorpc.Sdk;
using U5CNextResponse = Utxorpc.Sdk.Models.NextResponse;
using Argus.Sync.Utils;
using Block = Chrysalis.Cbor.Types.Cardano.Core.Block;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Argus.Sync.Providers;

/// <summary>
/// Cardano chain provider using the UTxO RPC (U5C) gRPC protocol.
/// </summary>
/// <param name="url">The gRPC endpoint URL.</param>
/// <param name="header">The HTTP headers to include with gRPC requests.</param>
public class U5CProvider(string url, Dictionary<string, string> header) : ICardanoChainProvider
{
    /// <inheritdoc />
    public Task<Point> GetTipAsync(ulong networkMagic = 2, CancellationToken? stoppingToken = null) => throw new NotImplementedException();

    /// <inheritdoc />
    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(IEnumerable<Point> intersection, ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {

        SyncServiceClient client = new(
            url,
            header
        );

        Point? latestIntersection = intersection.MaxBy(e => e.Slot);
        await foreach (U5CNextResponse? response in client.FollowTipAsync(
            new Utxorpc.Sdk.Models.BlockRef
            (
                latestIntersection!.Hash,
                latestIntersection.Slot
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
                        Block? block = ArgusUtil.DeserializeBlockWithEra(response.AppliedBlock!.NativeBytes);
                        yield return new NextResponse(
                            NextResponseAction.RollForward,
                            null,
                            block!
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Undo:
                        block = ArgusUtil.DeserializeBlockWithEra(response.UndoneBlock!.NativeBytes);
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            RollBackType.Inclusive,
                            block!
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Reset:
                        block = new ConwayBlock(
                            new BlockHeader(
                                new AlonzoHeaderBody(
                                    0,
                                    response.ResetRef!.Index,
                                    [],
                                    [],
                                    [],
                                    new VrfCert([], []),
                                    new VrfCert([], []),
                                    0,
                                    [],
                                    [],
                                    0,
                                    0,
                                    [],
                                    0,
                                    0
                                ),
                            []
                            ),
                            new CborDefList<ConwayTransactionBody>([]),
                            new CborDefList<PostAlonzoTransactionWitnessSet>([]),
                            new AuxiliaryDataSet([]),
                            new CborDefList<int>([])
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
