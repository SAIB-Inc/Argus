using System.Formats.Cbor;
using Argus.Sync.Data.Models;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Header;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;
using Block = Chrysalis.Cbor.Types.Cardano.Core.Block;
using CPoint = Chrysalis.Network.Cbor.Common.Point;
using Point = Argus.Sync.Data.Models.Point;

namespace Argus.Sync.Providers;

public class N2CProvider(string NodeSocketPath) : ICardanoChainProvider
{
    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(Point intersection, CancellationToken? stoppingToken)
    {
        stoppingToken ??= new CancellationTokenSource().Token;

        NodeClient client = await NodeClient.ConnectAsync(NodeSocketPath, stoppingToken.Value);
        client.Start();

        ProposeVersions proposeVersion = HandshakeMessages.ProposeVersions(VersionTables.N2C_V10_AND_ABOVE);
        CborWriter writer = new();
        ProposeVersions.Write(writer, proposeVersion);
        await client.Handshake!.SendAsync(proposeVersion, CancellationToken.None);

        CPoint point = new(intersection.Slot, Convert.FromHexString(intersection.Hash));
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