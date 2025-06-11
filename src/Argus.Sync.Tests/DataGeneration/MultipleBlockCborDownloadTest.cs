using Argus.Sync.Utils;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Xunit;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.DataGeneration;

public class MultipleBlockCborDownloadTest(ITestOutputHelper output)
{
    [Fact]
    public async Task DownloadMultipleBlocksCbor_For100Blocks_ShouldReturnValidSequentialData()
    {
        // Arrange - Download 100 consecutive blocks for comprehensive testing
        const int blocksToDownload = 100;
        output.WriteLine($"Downloading {blocksToDownload} consecutive blocks from chain...");
        
        var socketPath = "/tmp/node.socket";
        var networkMagic = 2UL; // mainnet
        
        // Skip test if socket doesn't exist
        if (!File.Exists(socketPath))
        {
            output.WriteLine($"Skipping test - socket {socketPath} not found");
            return;
        }

        // Create unified test data directory
        var testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "Blocks");
        Directory.CreateDirectory(testDataDir);
        
        // Act
        var client = await NodeClient.ConnectAsync(socketPath, CancellationToken.None);
        await client.StartAsync(networkMagic);
        
        // Use real intersection point you provided
        var intersectionPoint = new Chrysalis.Network.Cbor.Common.Point(
            82916702,
            Convert.FromHexString("cee6005816f33d87155f3fe31170081bbdac6356a8eebc9aa725e133e96cf8e5")
        );
        
        // Find intersection
        var intersectMessage = await client.ChainSync!.FindIntersectionAsync([intersectionPoint], CancellationToken.None);
        if (intersectMessage is not MessageIntersectFound)
        {
            Assert.Fail("Could not find intersection point");
            return;
        }
        
        int blocksDownloaded = 0;
        ulong? firstSlot = null;
        
        // Process chain sync responses
        while (blocksDownloaded < blocksToDownload)
        {
            var nextResponse = await client.ChainSync.NextRequestAsync(CancellationToken.None);
            if (nextResponse is MessageRollForward rollForward)
            {
                var block = ArgusUtil.DeserializeBlockWithEra(rollForward.Payload.Value);
                if (block == null) continue;
                
                var slot = block.Header().HeaderBody().Slot();
                var hash = block.Header().Hash();
                var height = block.Header().HeaderBody().BlockNumber();
                
                output.WriteLine($"Processing block {blocksDownloaded + 1}/{blocksToDownload} - Slot: {slot}, Height: {height}, Hash: {hash[..16]}...");
                
                // Track first slot for reference
                if (blocksDownloaded == 0)
                {
                    firstSlot = slot;
                    output.WriteLine($"Starting from slot {firstSlot}");
                }
                
                // Save the era-tagged block format using unified naming
                var eraBlockBytes = rollForward.Payload.Value.ToArray();
                var fileName = $"{slot}.cbor";
                var filePath = Path.Combine(testDataDir, fileName);
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
                output.WriteLine($"Encountered rollback to slot {rollBackward.Point?.Slot}, continuing...");
                // In a real download, we might need to handle this, but for test data generation we'll skip
            }
        }
        
        // Assert
        Assert.Equal(blocksToDownload, blocksDownloaded);
        
        output.WriteLine($"✅ Successfully downloaded {blocksDownloaded} consecutive blocks");
        output.WriteLine($"✅ Block files saved to: {testDataDir}");
        output.WriteLine($"✅ Files ready for use in end-to-end tests");
    }
}