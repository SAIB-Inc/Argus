namespace Argus.Sync.Data.Models;

public record NextResponse
(
    NextResponseAction Action,
    RollBackType? RollBackType,
    Chrysalis.Cbor.Types.Cardano.Core.Block Block
);

