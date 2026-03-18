namespace Argus.Sync.Data.Models;

public record NextResponse
(
    NextResponseAction Action,
    RollBackType? RollBackType,
    Chrysalis.Codec.Types.Cardano.Core.IBlock? Block,
    ulong? RollbackSlot = null
);

