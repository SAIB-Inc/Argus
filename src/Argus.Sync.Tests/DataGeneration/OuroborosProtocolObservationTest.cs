using Argus.Sync.Utils;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Xunit.Abstractions;
using Chrysalis.Codec.Types.Cardano.Core;

namespace Argus.Sync.Tests.DataGeneration;

public class OuroborosProtocolObservationTest(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "DataGeneration")]
    public async Task ObserveOuroborosProtocol_RollbackAndFirstTwoRollforwards_ShouldShowActualBehavior()
    {
        // Arrange - Use real intersection point you provided
        string socketPath = "/tmp/node.socket";
        ulong networkMagic = 2UL; // mainnet

        // Skip test if socket doesn't exist
        if (!File.Exists(socketPath))
        {
            output.WriteLine($"Skipping test - socket {socketPath} not found");
            return;
        }

        SpecificPoint intersectionPoint = new(
            82916702,
            Convert.FromHexString("cee6005816f33d87155f3fe31170081bbdac6356a8eebc9aa725e133e96cf8e5")
        );

        output.WriteLine("=== Observing Real Ouroboros Protocol Behavior ===");
        output.WriteLine($"Intersection: Slot {intersectionPoint.Slot}, Hash {Convert.ToHexString(intersectionPoint.Hash.Span)[..16]}...");

        // Act - Connect and observe
        NodeClient client = await NodeClient.ConnectAsync(socketPath, CancellationToken.None);
        await client.StartAsync(networkMagic);

        // Find intersection
        ChainSyncMessage intersectMessage = await client.ChainSync!.FindIntersectionAsync([intersectionPoint], CancellationToken.None);
        if (intersectMessage is not MessageIntersectFound)
        {
            output.WriteLine("Could not find intersection point");
            return;
        }

        output.WriteLine("Intersection found, starting chain sync...");

        int messageCount = 0;
        const int maxMessages = 3; // Observe first 3 messages (1 rollback + 2 rollforwards)

        // Process chain sync responses
        while (messageCount < maxMessages)
        {
            MessageNextResponse? nextResponse = await client.ChainSync.NextRequestAsync(CancellationToken.None);
            messageCount++;

            output.WriteLine($"\n--- Message {messageCount} ---");

            switch (nextResponse)
            {
                case MessageRollBackward rollback:
                    output.WriteLine($"ROLLBACK Message:");
                    output.WriteLine($"   Type: {rollback.GetType().Name}");
                    output.WriteLine($"   Point Slot: {(rollback.Point is SpecificPoint sp ? sp.Slot : 0)}");
                    if (rollback.Point is SpecificPoint rbPoint)
                    {
                        output.WriteLine($"   Point Hash: {Convert.ToHexString(rbPoint.Hash.Span)[..16]}...");
                    }
                    output.WriteLine($"   This establishes the intersection point for chain sync");
                    break;

                case MessageRollForward rollforward:
                    output.WriteLine($"ROLLFORWARD Message:");
                    output.WriteLine($"   Type: {rollforward.GetType().Name}");
                    output.WriteLine($"   Tip Slot: {rollforward.Tip?.Slot}");
                    output.WriteLine($"   Tip: {(rollforward.Tip != null ? $"Slot {rollforward.Tip.Slot}" : "null")}");

                    // Try to deserialize the block
                    IBlock? block = ArgusUtil.DeserializeBlockWithEra(rollforward.Payload.Value);
                    if (block != null)
                    {
                        ulong slot = block.Header().HeaderBody().Slot();
                        string hash = block.Header().Hash();
                        ulong height = block.Header().HeaderBody().BlockNumber();
                        int txCount = block.TransactionBodies()?.Count() ?? 0;

                        output.WriteLine($"   Block Slot: {slot}");
                        output.WriteLine($"   Block Hash: {hash[..16]}...");
                        output.WriteLine($"   Block Height: {height}");
                        output.WriteLine($"   Transaction Count: {txCount}");
                        output.WriteLine($"   CBOR Size: {rollforward.Payload.Value.Length} bytes");
                        output.WriteLine($"   Era-tagged format: {Convert.ToHexString(rollforward.Payload.Value.Span)[..20]}...");
                    }
                    else
                    {
                        output.WriteLine($"   Failed to deserialize block");
                        output.WriteLine($"   Raw CBOR: {Convert.ToHexString(rollforward.Payload.Value.Span)[..40]}...");
                    }
                    break;

                case MessageAwaitReply awaitMsg:
                    output.WriteLine($"AWAIT Message:");
                    output.WriteLine($"   Type: {awaitMsg.GetType().Name}");
                    output.WriteLine($"   Node is waiting for more data");
                    break;

                default:
                    output.WriteLine($"UNKNOWN Message:");
                    output.WriteLine($"   Type: {nextResponse?.GetType().Name ?? "null"}");
                    break;
            }
        }

        output.WriteLine("\n=== Protocol Observation Complete ===");
        output.WriteLine("Key observations:");
        output.WriteLine("1. First message should be MessageRollBackward to establish intersection");
        output.WriteLine("2. Subsequent messages should be MessageRollForward with actual blocks");
        output.WriteLine("3. Each rollforward contains era-tagged CBOR block data");
        output.WriteLine("4. Block data includes transaction count and other metadata");

        // Clean up
        client.Dispose();
    }
}
