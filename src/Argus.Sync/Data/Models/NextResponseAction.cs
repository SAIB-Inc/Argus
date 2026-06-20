namespace Argus.Sync.Data.Models;

/// <summary>
/// Represents the type of action in a chain sync response.
/// </summary>
public enum NextResponseAction
{
    /// <summary>Awaiting next response from chain sync.</summary>
    Await,
    /// <summary>A new block has been received (roll forward).</summary>
    RollForward,
    /// <summary>A chain reorganization requires rolling back.</summary>
    RollBack
}
