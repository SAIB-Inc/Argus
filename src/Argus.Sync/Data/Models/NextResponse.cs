using Argus.Sync.Data.Models;
using Block = Chrysalis.Cardano.Core.Block;

public record NextResponse
(
    NextResponseAction Action,
    RollBackType? RollBackType,
    Block Block
);

