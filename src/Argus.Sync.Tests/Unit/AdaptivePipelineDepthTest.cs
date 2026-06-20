using Argus.Sync.Providers;

namespace Argus.Sync.Tests.Unit;

/// <summary>
/// Pins <see cref="N2NProvider.AdaptivePipelineDepth"/> — the function that scales the N2N chain-sync
/// pipeline depth by the slot-gap to the node's tip. Pure, no node required (CI-runnable). It must
/// collapse to 1 at the tip (so we never over-request), grow with the gap, never decrease as the gap
/// grows, and never exceed the configured maximum.
/// </summary>
public class AdaptivePipelineDepthTest
{
    [Theory]
    [InlineData(0UL, 1)]      // at the tip -> a single in-flight request
    [InlineData(4UL, 1)]
    [InlineData(20UL, 2)]
    [InlineData(100UL, 5)]
    [InlineData(500UL, 20)]
    [InlineData(2_000UL, 100)]
    public void MapsTipGapToDepth_UnderAMaxOf100(ulong tipGap, int expected)
        => Assert.Equal(expected, N2NProvider.AdaptivePipelineDepth(maxDepth: 100, tipGap));

    [Fact]
    public void FarFromTip_ClampsToTheConfiguredMax()
    {
        Assert.Equal(100, N2NProvider.AdaptivePipelineDepth(maxDepth: 100, tipGap: 1_000_000));
        Assert.Equal(50, N2NProvider.AdaptivePipelineDepth(maxDepth: 50, tipGap: 1_000_000));
        Assert.Equal(500, N2NProvider.AdaptivePipelineDepth(maxDepth: 1_000, tipGap: 10_000));
    }

    [Fact]
    public void NeverDecreasesAsTheGapGrows()
    {
        int previous = 0;
        foreach (ulong gap in new ulong[] { 0, 4, 20, 100, 500, 2_000, 10_000, 50_000, 1_000_000 })
        {
            int depth = N2NProvider.AdaptivePipelineDepth(maxDepth: 1_000, tipGap: gap);
            Assert.True(depth >= previous, $"depth must be monotonic non-decreasing in the gap (gap {gap} gave {depth} < {previous})");
            previous = depth;
        }
    }

    [Fact]
    public void NeverBelowOne_EvenWithATinyMax()
        => Assert.Equal(1, N2NProvider.AdaptivePipelineDepth(maxDepth: 1, tipGap: 1_000_000));
}
