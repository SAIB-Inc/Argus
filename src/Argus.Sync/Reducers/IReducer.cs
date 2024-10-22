using Argus.Sync.Data.Models;
using Block = Chrysalis.Cardano.Models.Core.BlockEntity;

namespace Argus.Sync.Reducers;

public interface IReducer<out T> where T : IReducerModel
{
    Task RollForwardAsync(Block block);
    Task RollBackwardAsync(ulong slot);
}