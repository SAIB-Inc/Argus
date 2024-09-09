using Cardano.Sync.Data.Models;
using PallasDotnet.Models;

namespace Cardano.Sync.Reducers;

public interface IReducer<out T> where T : IReducerModel
{
    Task RollForwardAsync(NextResponse response);
    Task RollBackwardAsync(NextResponse response);
}