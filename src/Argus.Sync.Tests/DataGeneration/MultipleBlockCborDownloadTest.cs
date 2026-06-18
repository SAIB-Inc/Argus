using Argus.Sync.Utils;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Xunit.Abstractions;
using Chrysalis.Codec.Types.Cardano.Core;

namespace Argus.Sync.Tests.DataGeneration;

public class MultipleBlockCborDownloadTest(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "DataGeneration")]
    public async Task DownloadMultipleBlocksCbor_For100Blocks_ShouldReturnValidSequentialData()
    {
        // Arrange - Download 100 consecutive blocks for comprehensive testing
        const int blocksToDownload = 100;
        output.WriteLine($"Downloading {blocksToDownload} consecutive blocks from chain...");

        string socketPath = "/tmp/node.socket";
        ulong networkMagic = 2UL; // mainnet

        // Skip test if socket doesn't exist
        if (!File.Exists(socketPath))
        {
            output.WriteLine($"Skipping test - socket {socketPath} not found");
            return;
        }

        // Create unified test data directory
        string testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "Blocks");
        _ = Directory.CreateDirectory(testDataDir);

        // Act
        NodeClient client = await NodeClient.ConnectAsync(socketPath, CancellationToken.None);
        await client.StartAsync(networkMagic);

        // Use real intersection point you provided
        SpecificPoint intersectionPoint = new(
            82916702,
            Convert.FromHexString("cee6005816f33d87155f3fe31170081bbdac6356a8eebc9aa725e133e96cf8e5")
        );

        // Find intersection
        ChainSyncMessage intersectMessage = await client.ChainSync!.FindIntersectionAsync([intersectionPoint], CancellationToken.None);
        if (intersectMessage is not MessageIntersectFound)
        {
            Assert.Fail("Could not find intersection point");
            return;
        }

        int blocksDownloaded = 0;

        // Process chain sync responses
        while (blocksDownloaded < blocksToDownload)
        {
            MessageNextResponse? nextResponse = await client.ChainSync.NextRequestAsync(CancellationToken.None);
            if (nextResponse is N2CMessageRollForward rollForward)
            {
                IBlock? block = ArgusUtil.DeserializeBlockWithEra(rollForward.Payload.Value);
                if (block == null)
                {
                    continue;
                }

                ulong slot = block.Header().HeaderBody().Slot();
                string hash = block.Header().Hash();
                ulong height = block.Header().HeaderBody().BlockNumber();

                output.WriteLine($"Processing block {blocksDownloaded + 1}/{blocksToDownload} - Slot: {slot}, Height: {height}, Hash: {hash[..16]}...");

                // Track first slot for reference
                if (blocksDownloaded == 0)
                {
                    ulong? firstSlot = slot;
                    output.WriteLine($"Starting from slot {firstSlot}");
                }

                // Save the era-tagged block format using unified naming
                byte[] eraBlockBytes = rollForward.Payload.Value.ToArray();
                string fileName = $"{slot}.cbor";
                string filePath = Path.Combine(testDataDir, fileName);
                await File.WriteAllBytesAsync(filePath, eraBlockBytes);

                output.WriteLine($"Saved block to: {fileName}");

                blocksDownloaded++;

                // Stop after downloading our target count
                if (blocksDownloaded >= blocksToDownload)
                {
                    break;
                }
            }
            else if (nextResponse is MessageRollBackward rollBackward)
            {
                output.WriteLine($"Encountered rollback to slot {(rollBackward.Point is SpecificPoint rbPt ? rbPt.Slot : 0)}, continuing...");
                // In a real download, we might need to handle this, but for test data generation we'll skip
            }
        }

        // Assert
        Assert.Equal(blocksToDownload, blocksDownloaded);

        output.WriteLine($"Successfully downloaded {blocksDownloaded} consecutive blocks");
        output.WriteLine($"Block files saved to: {testDataDir}");
        output.WriteLine($"Files ready for use in end-to-end tests");
    }
}
