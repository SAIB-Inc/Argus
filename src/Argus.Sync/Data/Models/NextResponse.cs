using Chrysalis.Cbor.Cardano.Types.Block;

namespace Argus.Sync.Data.Models;

public record NextResponse
(
    NextResponseAction Action,
    RollBackType? RollBackType,
    Block Block
);

