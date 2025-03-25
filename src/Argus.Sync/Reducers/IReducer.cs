using Argus.Sync.Data.Models;
using Chrysalis.Cbor.Cardano.Types.Block;

namespace Argus.Sync.Reducers;

public interface IReducer<out T> where T : IReducerModel
{
    Task RollForwardAsync(Block block);
    Task RollBackwardAsync(ulong slot);
}
