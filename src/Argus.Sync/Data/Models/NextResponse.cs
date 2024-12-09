using Argus.Sync.Data.Models;
using Block = Chrysalis.Cardano.Core.Types.Block.Block;

public record NextResponse
(
    NextResponseAction Action,
    RollBackType? RollBackType,
    Block Block
);

