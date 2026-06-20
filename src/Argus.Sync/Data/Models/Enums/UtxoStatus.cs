namespace Argus.Sync.Data.Models.Enums;

/// <summary>
/// Represents the status of a UTxO (Unspent Transaction Output).
/// </summary>
public enum UtxoStatus
{
    /// <summary>The UTxO has not been spent.</summary>
    Unspent,
    /// <summary>The UTxO has been spent.</summary>
    Spent
}
