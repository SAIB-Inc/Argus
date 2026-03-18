namespace Argus.Sync.Data.Models;

/// <summary>
/// Represents a response from the chain sync protocol containing an action, optional rollback type, and block data.
/// </summary>
/// <param name="Action">The chain sync action type.</param>
/// <param name="RollBackType">The rollback type, if applicable.</param>
/// <param name="Block">The block data associated with this response.</param>
public record NextResponse
(
    NextResponseAction Action,
    RollBackType? RollBackType,
    Chrysalis.Cbor.Types.Cardano.Core.Block Block
);
