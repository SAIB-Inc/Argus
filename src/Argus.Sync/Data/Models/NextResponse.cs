using Argus.Sync.Data.Models;
using Block = Chrysalis.Cardano.Models.Core.BlockEntity;

public record NextResponse
(
    NextResponseAction Action,
    RollBackType? RollBackType,
    Block Block
);

