using System.Threading.Channels;
using Argus.Sync.Data.Models;
using Argus.Sync.Providers;
using Argus.Sync.Utils;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Extensions.Cardano.Core;

namespace Argus.Sync.Tests.Mocks;

/// <summary>
/// Mock chain sync provider that implements only ICardanoChainProvider interface.
/// Pure manual control mode - chain sync waits for external triggers for rollforward/rollback events.
/// Test controls exactly when blocks are delivered through trigger methods.
/// </summary>
public class MockChainSyncProvider : ICardanoChainProvider
{
    private readonly List<IBlock> _availableBlocks;
    private readonly ulong? _initialRollbackSlot;
    private readonly Channel<NextResponse> _controlChannel;
    private readonly ChannelWriter<NextResponse> _controlWriter;
    private readonly ChannelReader<NextResponse> _controlReader;

    /// <param name="testDataDirectory">Directory whose <c>Blocks/*.cbor</c> are loaded.</param>
    /// <param name="initialRollbackSlot">
    /// Slot for the opening Ouroboros rollback (intersection). Defaults to the lowest available block;
    /// set it to simulate a node reconnecting at a specific checkpoint (e.g. a restart after a crash).
    /// </param>
    public MockChainSyncProvider(string testDataDirectory, ulong? initialRollbackSlot = null)
    {
        _availableBlocks = DiscoverAllBlocks(testDataDirectory);

        if (_availableBlocks.Count == 0)
        {
            throw new InvalidOperationException($"No blocks found in {testDataDirectory}");
        }

        _initialRollbackSlot = initialRollbackSlot;
        _controlChannel = Channel.CreateUnbounded<NextResponse>();
        _controlWriter = _controlChannel.Writer;
        _controlReader = _controlChannel.Reader;
    }

    private static List<IBlock> DiscoverAllBlocks(string testDataDirectory)
    {
        List<IBlock> blocks = [];
        string blocksDir = Path.Combine(testDataDirectory, "Blocks");

        if (!Directory.Exists(blocksDir))
        {
            return blocks;
        }

        string[] cborFiles = [.. Directory.GetFiles(blocksDir, "*.cbor").OrderBy(f => f)];

        foreach (string? filePath in cborFiles)
        {
            try
            {
                byte[] blockBytes = File.ReadAllBytes(filePath);
                IBlock? block = ArgusUtil.DeserializeBlockWithEra(blockBytes);
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
        return [.. blocks.OrderBy(b => b.Header().HeaderBody().Slot())];
    }

    public async IAsyncEnumerable<NextResponse> StartChainSyncAsync(IEnumerable<Point> intersection, ulong networkMagic = 2, CancellationToken? stoppingToken = null)
    {
        // Send initial rollback to establish intersection (standard Ouroboros behavior). Defaults to
        // the lowest available block, or the configured checkpoint to simulate a reconnect after a crash.
        ulong intersectionSlot = _initialRollbackSlot ?? _availableBlocks.First().Header().HeaderBody().Slot();
        yield return new NextResponse(NextResponseAction.RollBack, RollBackType.Exclusive, null, intersectionSlot);

        // Then wait for external control signals - test must trigger all subsequent events
        await foreach (NextResponse response in _controlReader.ReadAllAsync(stoppingToken ?? CancellationToken.None))
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

        IBlock lastBlock = _availableBlocks.Last();
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
        IBlock block = _availableBlocks.FirstOrDefault(b => b.Header().HeaderBody().Slot() == slot) ?? throw new InvalidOperationException($"No block found with slot {slot}");
        await _controlWriter.WriteAsync(new NextResponse(NextResponseAction.RollForward, null, block));
    }

    /// <summary>
    /// Triggers rollback to a specific slot.
    /// </summary>
    public async Task TriggerRollBackAsync(ulong rollbackSlot, RollBackType rollbackType = RollBackType.Exclusive)
    {
        // Find the block that matches the rollback slot for proper CardanoIndexWorker compatibility
        IBlock? rollbackBlock = _availableBlocks.FirstOrDefault(b => b.Header().HeaderBody().Slot() == rollbackSlot) ?? _availableBlocks
                .Where(b => b.Header().HeaderBody().Slot() <= rollbackSlot)
                .OrderByDescending(b => b.Header().HeaderBody().Slot())
                .FirstOrDefault() ?? _availableBlocks.First();

        // Create a response with null Block and the rollback slot
        NextResponse response = new(NextResponseAction.RollBack, rollbackType, null, rollbackSlot);

        await _controlWriter.WriteAsync(response);
    }

    /// <summary>
    /// Completes the chain sync, causing the async enumerable to end.
    /// </summary>
    public void CompleteChainSync() => _controlWriter.Complete();

    /// <summary>
    /// Gets the available blocks for test verification.
    /// </summary>
    public IReadOnlyList<IBlock> AvailableBlocks => _availableBlocks.AsReadOnly();
}
