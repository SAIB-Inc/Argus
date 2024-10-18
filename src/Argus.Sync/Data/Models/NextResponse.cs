using Argus.Sync.Data.Models;

public record NextResponse
(
    NextResponseAction Action,
    Chrysalis.Cardano.Models.Core.Block.Block? Block
);

