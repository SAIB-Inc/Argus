using Argus.Sync.Data.Models;
using IBlock = Chrysalis.Codec.Types.Cardano.Core.IBlock;

namespace Argus.Sync.Reducers;

/// <summary>
/// Interface for blockchain data reducers that process blocks and handle rollbacks.
/// </summary>
/// <typeparam name="T">The reducer model type.</typeparam>
public interface IReducer<out T> where T : IReducerModel
{
    /// <summary>
    /// Processes a new block during chain synchronization (roll forward).
    /// </summary>
    /// <param name="block">The block to process.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RollForwardAsync(IBlock block);

    /// <summary>
    /// Handles a chain reorganization by rolling back to the specified slot.
    /// </summary>
    /// <param name="slot">The slot to roll back to.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RollBackwardAsync(ulong slot);
}
