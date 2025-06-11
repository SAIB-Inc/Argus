using Argus.Sync.Utils;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.DataGeneration;

public class BlockCborDownloadTest(ITestOutputHelper output)
{
    [Fact]
    public async Task DownloadBlockCbor_ForTestBlock_ShouldReturnValidCborData()
    {
        // Arrange - Block we have in database: slot 82801348, height 3314966
        const ulong targetSlot = 82801348;
        const string expectedHash = "6afb5d5fb8f11608d0af3e7c60cf0879c6d565917a1c904ebb7ec127dccfee23";
        
        var socketPath = "/tmp/node.socket";
        var networkMagic = 2UL; // mainnet
        
        // Skip test if socket doesn't exist
        if (!File.Exists(socketPath))
        {
            output.WriteLine($"Skipping test - socket {socketPath} not found");
            return;
        }

        // Act
        var client = await NodeClient.ConnectAsync(socketPath, CancellationToken.None);
        await client.StartAsync(networkMagic);
        
        // Find the intersection point just before our target block
        var intersectionPoint = new Chrysalis.Network.Cbor.Common.Point(
            82801045,
            Convert.FromHexString("3bf10d004679509605ad3d3bbd16048408914e74e8b8c85ea31c9ca9c04a92bf")
        );
        
        // Find intersection
        var intersectMessage = await client.ChainSync!.FindIntersectionAsync([intersectionPoint], CancellationToken.None);
        if (intersectMessage is not MessageIntersectFound)
        {
            Assert.Fail("Could not find intersection point");
            return;
        }
        
        // Process chain sync responses
        while (true)
        {
            var nextResponse = await client.ChainSync.NextRequestAsync(CancellationToken.None);
            if (nextResponse is MessageRollForward rollForward)
            {
                var block = ArgusUtil.DeserializeBlockWithEra(rollForward.Payload.Value);
                if (block == null) continue;
                
                var slot = block.Header().HeaderBody().Slot();
                var hash = block.Header().Hash();
                
                output.WriteLine($"Processing block at slot {slot} with hash {hash}");
                
                if (slot == targetSlot)
                {
                    // Assert
                    Assert.Equal(expectedHash, hash);
                    Assert.NotNull(block.Raw);
                    Assert.True(block.Raw.HasValue && block.Raw.Value.Length > 0);
                    
                    // Save the era-tagged block format (rollForward.Payload.Value) for test data
                    var eraBlockBytes = rollForward.Payload.Value.ToArray();
                    var eraBlockHex = Convert.ToHexString(eraBlockBytes).ToLowerInvariant();
                    output.WriteLine($"Era-tagged block CBOR length: {eraBlockBytes.Length} bytes");
                    output.WriteLine($"Era-tagged block CBOR hex (first 200 chars): {eraBlockHex[..Math.Min(200, eraBlockHex.Length)]}...");
                    
                    // Save to unified test data directory
                    var testDataDir = Path.Combine(Directory.GetCurrentDirectory(), "TestData", "Blocks");
                    Directory.CreateDirectory(testDataDir);
                    var testDataPath = Path.Combine(testDataDir, $"{targetSlot}.cbor");
                    await File.WriteAllBytesAsync(testDataPath, eraBlockBytes);
                    output.WriteLine($"Saved block CBOR to: {testDataPath}");
                    
                    break;
                }
                
                // Stop after finding our target or going past it
                if (slot > targetSlot)
                {
                    Assert.Fail($"Passed target slot {targetSlot} without finding the block");
                    break;
                }
            }
        }
    }
}