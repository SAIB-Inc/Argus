using Argus.Sync.Data.Models;
using Argus.Sync.Providers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core;

namespace Argus.Sync.Tests.Mocks;

/// <summary>
/// Mock chain sync provider that implements only ICardanoChainProvider interface.
/// Discovers all available blocks from TestData/Blocks/ directory and yields them via chain sync.
/// Consumer controls how many blocks to process by breaking out of the async enumerable.
/// </summary>
public class MockChainSyncProvider : ICardanoChainProvider
{
    private readonly List<Block> _availableBlocks;

    public MockChainSyncProvider(string testDataDirectory)
    {
        _availableBlocks = DiscoverAllBlocks(testDataDirectory);
        
        if (_availableBlocks.Count == 0)
        {
            throw new InvalidOperationException($"No blocks found in {testDataDirectory}");
        }
    }

    private static List<Block> DiscoverAllBlocks(string testDataDirectory)
    {
        var blocks = new List<Block>();
        var blocksDir = Path.Combine(testDataDirectory, "Blocks");
        
        if (!Directory.Exists(blocksDir))
        {
            return blocks;
        }
        
        var cborFiles = Directory.GetFiles(blocksDir, "*.cbor")
            .OrderBy(f => f) // Lexical order should match slot order
            .ToArray();
        
        foreach (var filePath in cborFiles)
        {
            try
            {
                var blockBytes = File.ReadAllBytes(filePath);
                var block = ArgusUtil.DeserializeBlockWithEra(blockBytes);
                if (block != null)
                {
                    blocks.Add(block);
                }
            }
            catch (Exception ex)
            {
                // Skip corrupted files
                Console.WriteLine($"Warning: Could not load block from {filePath}: {ex.Message}");
            }
        }
        
        // Sort by slot to ensure proper order
        return blocks.OrderBy(b => b.Header().HeaderBody().Slot()).ToList();
    }

    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(IEnumerable<Point> intersection, ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        // First, yield rollback to intersection point (standard Ouroboros behavior)
        // Use the first available block as dummy for rollback
        var dummyBlock = _availableBlocks.First();
        yield return new NextResponse(NextResponseAction.RollBack, RollBackType.Exclusive, dummyBlock);
        
        // Then yield all available blocks one by one
        // Consumer controls how many to process by breaking out of the loop
        foreach (var block in _availableBlocks)
        {
            yield return new NextResponse(NextResponseAction.RollForward, null, block);
            await Task.Yield(); // Allow consumer to process
            
            // Check cancellation
            if (stoppingToken?.IsCancellationRequested == true)
            {
                yield break;
            }
        }
    }

    public async Task<Point> GetTipAsync(ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        await Task.CompletedTask;
        
        if (_availableBlocks.Count == 0)
        {
            throw new InvalidOperationException("No blocks available");
        }
        
        var lastBlock = _availableBlocks.Last();
        return new Point(
            lastBlock.Header().Hash(),
            lastBlock.Header().HeaderBody().Slot()
        );
    }
}