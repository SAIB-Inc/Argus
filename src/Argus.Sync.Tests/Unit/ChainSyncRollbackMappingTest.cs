using Argus.Sync.Data.Models;
using Argus.Sync.Utils;
using Chrysalis.Network.Cbor.Common;

namespace Argus.Sync.Tests.Unit;

/// <summary>
/// Regression tests for P0-4 — the ChainSync rollback-point → <see cref="NextResponse"/> mapping
/// shared by the N2C and N2N providers (<see cref="ArgusUtil.RollBackwardResponse"/>).
///
/// Standard Ouroboros chain-sync keeps the rollback point itself (verified against
/// ouroboros-network's <c>Ouroboros.Network.Mock.Chain.rollback</c>, Pallas, and Dolos):
///   • <b>SpecificPoint(X)</b> → <see cref="RollBackType.Exclusive"/> (worker deletes slots &gt; X, keeps X).
///   • <b>OriginPoint</b>      → <see cref="RollBackType.Inclusive"/> at slot 0 (worker deletes everything, ≥ 0).
///
/// Before the fix, OriginPoint fell through <c>default: continue</c> in N2CProvider and a
/// rollback-to-genesis was silently dropped.
/// </summary>
public sealed class ChainSyncRollbackMappingTest
{
    [Fact]
    public void SpecificPoint_MapsToExclusiveRollback_AtThatSlot()
    {
        byte[] hash = Convert.FromHexString("7ef942e6a670af6310737e9230b22e11a4bb1af69bed9affb09b1025b371d1cd");
        SpecificPoint point = new(126025608UL, hash);

        NextResponse response = ArgusUtil.RollBackwardResponse(point);

        Assert.Equal(NextResponseAction.RollBack, response.Action);
        Assert.Equal(RollBackType.Exclusive, response.RollBackType);
        Assert.Equal(126025608UL, response.RollbackSlot);
        Assert.Null(response.Block);
    }

    [Fact]
    public void OriginPoint_MapsToInclusiveRollback_AtSlotZero()
    {
        // A rollback to genesis must discard the entire chain. Inclusive/0 makes the worker
        // delete slot >= 0 (everything). Exclusive/0 would compute slot 1 and wrongly retain slot 0.
        NextResponse response = ArgusUtil.RollBackwardResponse(new OriginPoint());

        Assert.Equal(NextResponseAction.RollBack, response.Action);
        Assert.Equal(RollBackType.Inclusive, response.RollBackType);
        Assert.Equal(0UL, response.RollbackSlot);
        Assert.Null(response.Block);
    }
}
