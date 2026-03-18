namespace Argus.Sync.Data.Models.Enums;

/// <summary>
/// Represents the Cardano network type, identified by network magic number.
/// </summary>
public enum NetworkType
{
    /// <summary>No network specified.</summary>
    None = 0,
    /// <summary>Cardano mainnet.</summary>
    MAINNET = 764824073,
    /// <summary>Cardano pre-production testnet.</summary>
    PREPROD = 1,
    /// <summary>Cardano preview testnet.</summary>
    PREVIEW = 2
}
