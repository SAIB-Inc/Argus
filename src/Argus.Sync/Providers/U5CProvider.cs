using Argus.Sync.Data.Models;
using Utxorpc.Sdk;
using U5CNextResponse = Utxorpc.Sdk.Models.NextResponse;
using Argus.Sync.Utils;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core;

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
                        IBlock? block = ArgusUtil.DeserializeBlockWithEra(response.AppliedBlock!.NativeBytes);
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
                            block!,
                            block?.Slot()
                        );
                        break;
                    case Utxorpc.Sdk.Models.Enums.NextResponseAction.Reset:
                        yield return new NextResponse(
                            NextResponseAction.RollBack,
                            RollBackType.Exclusive,
                            null,
                            response.ResetRef!.Slot
                        );
                        break;
                    default:
                        continue;
                }
            }
        }
    }
}
