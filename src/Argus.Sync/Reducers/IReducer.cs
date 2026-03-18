using Argus.Sync.Data.Models;
using Block = Chrysalis.Codec.Types.Cardano.Core.IBlock;

namespace Argus.Sync.Reducers;

public interface IReducer<out T> where T : IReducerModel
{
    Task RollForwardAsync(Block block);
    Task RollBackwardAsync(ulong slot);
}
