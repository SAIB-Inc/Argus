using System.Threading.Channels;
using Argus.Sync.Data.Models;
using Argus.Sync.Providers;
using Argus.Sync.Utils;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
using Chrysalis.Cbor.Extensions.Cardano.Core;

namespace Argus.Sync.Tests.Mocks;

/// <summary>
/// Mock chain sync provider that implements only ICardanoChainProvider interface.
/// Pure manual control mode - chain sync waits for external triggers for rollforward/rollback events.
/// Test controls exactly when blocks are delivered through trigger methods.
/// </summary>
public class MockChainSyncProvider : ICardanoChainProvider
{
    private readonly List<Block> _availableBlocks;
    private readonly Channel<NextResponse> _controlChannel;
    private readonly ChannelWriter<NextResponse> _controlWriter;
    private readonly ChannelReader<NextResponse> _controlReader;
    private readonly Dictionary<NextResponse, ulong> _rollbackSlotOverrides = new();

    public MockChainSyncProvider(string testDataDirectory)
    {
        _availableBlocks = DiscoverAllBlocks(testDataDirectory);
        
        if (_availableBlocks.Count == 0)
        {
            throw new InvalidOperationException($"No blocks found in {testDataDirectory}");
        }

        _controlChannel = Channel.CreateUnbounded<NextResponse>();
        _controlWriter = _controlChannel.Writer;
        _controlReader = _controlChannel.Reader;
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
        // Send initial rollback to establish intersection (standard Ouroboros behavior)
        var dummyBlock = _availableBlocks.First();
        yield return new NextResponse(NextResponseAction.RollBack, RollBackType.Exclusive, dummyBlock);
        
        // Then wait for external control signals - test must trigger all subsequent events
        await foreach (var response in _controlReader.ReadAllAsync(stoppingToken ?? CancellationToken.None))
        {
            yield return response;
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

    // Control methods for triggering chain sync events
    
    /// <summary>
    /// Triggers rollforward of a specific block by slot.
    /// </summary>
    public async Task TriggerRollForwardAsync(ulong slot)
    {
        var block = _availableBlocks.FirstOrDefault(b => b.Header().HeaderBody().Slot() == slot);
        if (block is null)
        {
            throw new InvalidOperationException($"No block found with slot {slot}");
        }

        await _controlWriter.WriteAsync(new NextResponse(NextResponseAction.RollForward, null, block));
    }

    /// <summary>
    /// Triggers rollback to a specific slot.
    /// </summary>
    public async Task TriggerRollBackAsync(ulong rollbackSlot, RollBackType rollbackType = RollBackType.Exclusive)
    {
        // For testing purposes, use any available block as a reference
        // The test logic will use the actual rollbackSlot, not the block's slot
        var referenceBlock = _availableBlocks.First();

        // Create a response with the reference block
        var response = new NextResponse(NextResponseAction.RollBack, rollbackType, referenceBlock);
        
        // Store the actual intended rollback slot for this response
        _rollbackSlotOverrides[response] = rollbackSlot;

        await _controlWriter.WriteAsync(response);
    }

    /// <summary>
    /// Gets the actual intended rollback slot for a response, or falls back to the block's slot.
    /// </summary>
    public ulong GetActualRollbackSlot(NextResponse response)
    {
        if (_rollbackSlotOverrides.TryGetValue(response, out var overrideSlot))
        {
            return overrideSlot;
        }
        return response.Block?.Header().HeaderBody().Slot() ?? 0;
    }

    /// <summary>
    /// Completes the chain sync, causing the async enumerable to end.
    /// </summary>
    public void CompleteChainSync()
    {
        _controlWriter.Complete();
    }

    /// <summary>
    /// Gets the available blocks for test verification.
    /// </summary>
    public IReadOnlyList<Block> AvailableBlocks => _availableBlocks.AsReadOnly();
}