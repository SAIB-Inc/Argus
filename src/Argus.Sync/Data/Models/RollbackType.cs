namespace Argus.Sync.Data.Models;

/// <summary>
/// Specifies whether a rollback includes or excludes the rollback point itself.
/// </summary>
public enum RollBackType
{
    /// <summary>The rollback point is included in the rollback (rolled back along with subsequent blocks).</summary>
    Inclusive,
    /// <summary>The rollback point is excluded from the rollback (preserved as the last valid block).</summary>
    Exclusive
}
