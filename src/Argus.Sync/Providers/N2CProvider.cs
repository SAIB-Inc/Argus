using System.Formats.Cbor;
using Argus.Sync.Data.Models;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.MiniProtocols.Extensions;
using Chrysalis.Network.Multiplexer;
using Block = Chrysalis.Cbor.Types.Cardano.Core.Block;
using CPoint = Chrysalis.Network.Cbor.Common.Point;
using Point = Argus.Sync.Data.Models.Point;

namespace Argus.Sync.Providers;

public class N2CProvider(string NodeSocketPath) : ICardanoChainProvider
{

    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(IEnumerable<Point> intersections, ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        stoppingToken ??= new CancellationTokenSource().Token;

        NodeClient client = await NodeClient.ConnectAsync(NodeSocketPath, stoppingToken.Value);
        await client.StartAsync(networkMagic);

        IEnumerable<CPoint> cIntersections = intersections.Select(p => new CPoint(p.Slot, Convert.FromHexString(p.Hash)));

        while (true)
        {
            if (!cIntersections.Any())
            {
                break;
            }

            cIntersections = cIntersections.OrderByDescending(p => p.Slot);
            ChainSyncMessage intersectMessage = await client.ChainSync!.FindIntersectionAsync(cIntersections, stoppingToken.Value);

            if (intersectMessage is MessageIntersectFound)
            {
                break;
            }

            cIntersections = cIntersections.Skip(1);
        }

        while (!stoppingToken.Value.IsCancellationRequested)
        {
            MessageNextResponse? nextResponse = await client.ChainSync!.NextRequestAsync(stoppingToken.Value);
            switch (nextResponse)
            {
                case MessageRollBackward msg:
                    yield return new NextResponse(
                          NextResponseAction.RollBack,
                          RollBackType.Exclusive,
                          new ConwayBlock(
                                new(new BabbageHeaderBody(0, msg.Point.Slot, [], [], [], new([], []), 0, [], new([], 0, 0, []), new(0, 0)), []),
                                new CborDefList<ConwayTransactionBody>([]),
                                new CborDefList<PostAlonzoTransactionWitnessSet>([]),
                                new([]),
                                new CborDefList<int>([])
                          )
                      );
                    break;
                case MessageRollForward msg:
                    Block? block = ArgusUtil.DeserializeBlockWithEra(msg.Payload.Value);
                    yield return new NextResponse(
                          NextResponseAction.RollForward,
                          null,
                          block!
                      );
                    break;
                default:
                    continue;
            }
        }
    }

    public async Task<Point> GetTipAsync(ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        stoppingToken ??= new CancellationTokenSource().Token;

        NodeClient client = await NodeClient.ConnectAsync(NodeSocketPath, stoppingToken.Value);
        await client.StartAsync(networkMagic);

        Tip tip = await client.LocalStateQuery!.GetTipAsync();

        return new(Convert.ToHexString(tip.Slot.Hash).ToLowerInvariant(), tip.Slot.Slot);
    }
}