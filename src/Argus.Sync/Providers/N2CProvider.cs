using System.Formats.Cbor;
using Argus.Sync.Data.Models;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Cardano.Types.Block;
using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;
using CPoint = Chrysalis.Network.Cbor.Common.Point;
using Point = Argus.Sync.Data.Models.Point;

namespace Argus.Sync.Providers;

public class N2CProvider(string NodeSocketPath) : ICardanoChainProvider
{
    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken)
    {
        Console.WriteLine("Starting Chain Sync.....................");
        stoppingToken ??= new CancellationTokenSource().Token;
        NodeClient client = await NodeClient.ConnectAsync(NodeSocketPath, stoppingToken.Value);
        client.Start();

        ProposeVersions proposeVersion = HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE);
        CborWriter writer = new();
        ProposeVersions.Write(writer, proposeVersion);

        Console.WriteLine("Sending Handshake.....................");
        await client.Handshake!.SendAsync(proposeVersion, CancellationToken.None);

        CPoint point = new(intersection.Slot, Convert.FromHexString(intersection.Hash));

        Console.WriteLine("Finding Intersection.....................");
        await client.ChainSync!.FindIntersectionAsync([point], stoppingToken.Value);

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
                    Block? block = ArgusUtils.DeserializeBlockWithEra(msg.Payload.Value);
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
}