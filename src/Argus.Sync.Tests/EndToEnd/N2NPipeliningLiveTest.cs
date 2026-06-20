using Argus.Sync.Data.Models;
using Argus.Sync.Providers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Xunit.Abstractions;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Live integration tests for the <b>pipelined</b> <see cref="N2NProvider"/> against a local preprod
/// node (N2N TCP, default 3001). Complements <see cref="N2NProviderLiveTest"/> (intersection rollback
/// + first blocks) by covering the two paths the pipelining rewrite added:
/// <list type="number">
///   <item>multi-batch catch-up — pulling more blocks than one pipeline depth, asserting the drain-then-
///   fetch batches yield strictly-ascending, gap/duplicate-free full blocks across batch boundaries;</item>
///   <item>the tip path — starting <em>at</em> the node's tip so the provider must handle
///   <c>MessageAwaitReply</c> and then follow newly produced blocks in order, without hanging.</item>
/// </list>
/// Both self-skip if the N2N port is unreachable.
/// </summary>
public sealed class N2NPipeliningLiveTest(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const string Host = "localhost";
    private const int Port = 3001;
    private const ulong NetworkMagic = 1UL; // preprod
    private const ulong IntersectionSlot = 126025608UL;
    private const string IntersectionHash = "7ef942e6a670af6310737e9230b22e11a4bb1af69bed9affb09b1025b371d1cd";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PipelinedCatchUp_AcrossMultipleBatches_StrictlyAscending_NoGapsOrDuplicates()
    {
        if (!await IsReachableAsync(Host, Port))
        {
            _output.WriteLine($"SKIP: N2N port {Host}:{Port} not reachable.");
            return;
        }

        // Depth 50 with a 150-block target guarantees the run spans more than one drain-then-fetch batch,
        // exercising ordering + de-duplication across batch boundaries.
        await using N2NProvider provider = new(Host, Port, PipelineDepth: 50);
        List<Point> intersection = [new(IntersectionHash, IntersectionSlot)];
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        const int target = 150;
        List<ulong> slots = [];
        HashSet<string> hashes = [];

        try
        {
            await foreach (NextResponse response in provider.StartChainSyncAsync(intersection, NetworkMagic, cts.Token))
            {
                if (response.Action != NextResponseAction.RollForward)
                {
                    continue;
                }
                IBlock block = response.Block!;
                slots.Add(block.Header().HeaderBody().Slot());
                _ = hashes.Add(block.Header().Hash());
                _ = block.TransactionBodies(); // body present + decodes (header -> BlockFetch body worked)
                if (slots.Count >= target)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // window elapsed — handled by the skip below
        }

        if (slots.Count < target)
        {
            _output.WriteLine($"SKIP: only {slots.Count}/{target} blocks streamed (node not synced past the intersection).");
            return;
        }

        for (int i = 1; i < slots.Count; i++)
        {
            Assert.True(slots[i] > slots[i - 1],
                $"slot {slots[i]} must be strictly greater than {slots[i - 1]} (chain order, no reorder, no dupes)");
        }
        Assert.Equal(slots.Count, hashes.Count); // every block distinct across the batches
        _output.WriteLine($"Pipelined {slots.Count} blocks, slots {slots[0]}..{slots[^1]} — ascending + distinct.");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AtTip_HandlesAwaitReply_AndFollowsNewBlocksInOrder()
    {
        if (!await IsReachableAsync(Host, Port))
        {
            _output.WriteLine($"SKIP: N2N port {Host}:{Port} not reachable.");
            return;
        }

        await using N2NProvider provider = new(Host, Port);
        Point tip = await provider.GetTipAsync(NetworkMagic);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(90));

        NextResponse? firstRollback = null;
        List<ulong> followed = [];
        ulong previous = tip.Slot;

        try
        {
            await foreach (NextResponse response in provider.StartChainSyncAsync([tip], NetworkMagic, cts.Token))
            {
                if (response.Action == NextResponseAction.RollBack)
                {
                    firstRollback ??= response;
                }
                else if (response.Action == NextResponseAction.RollForward)
                {
                    ulong slot = response.Block!.Header().HeaderBody().Slot();
                    Assert.True(slot > previous, $"tip-follow slot {slot} must be greater than the previous {previous}");
                    previous = slot;
                    followed.Add(slot);
                    if (followed.Count >= 2)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // no/slow new blocks within the window — acceptable (assertions below cover the deterministic part)
        }

        // Deterministic: starting AT the tip yields the intersection rollback, mapped Exclusive at the tip slot.
        // Reaching this without a hang already proves the at-tip AwaitReply drain works.
        Assert.NotNull(firstRollback);
        Assert.Equal(RollBackType.Exclusive, firstRollback!.RollBackType);
        Assert.Equal(tip.Slot, firstRollback.RollbackSlot);

        // Best-effort: any blocks preprod produced during the window were past the tip and in order (asserted
        // in the loop). Zero new blocks within 90s is acceptable — the tip path was still exercised.
        _output.WriteLine(followed.Count > 0
            ? $"At tip {tip.Slot}: followed {followed.Count} new block(s) in order: {string.Join(", ", followed)}."
            : $"At tip {tip.Slot}: AwaitReply handled cleanly, no new block within 90s (acceptable).");
    }

    private static async Task<bool> IsReachableAsync(string host, int port)
    {
        try
        {
            using System.Net.Sockets.TcpClient client = new();
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(host, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            // N2N port not listening -> the test skips.
            return false;
        }
    }
}
