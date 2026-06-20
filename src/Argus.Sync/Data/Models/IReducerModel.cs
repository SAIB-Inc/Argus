namespace Argus.Sync.Data.Models;

/// <summary>
/// Marker interface for reducer model types. Implemented by all entity models
/// that are managed by Argus reducers for blockchain data indexing.
/// All reducer models must expose a Slot for rollback operations.
/// </summary>
public interface IReducerModel
{
    /// <summary>Gets the blockchain slot number associated with this model entry.</summary>
    ulong Slot { get; }
}
