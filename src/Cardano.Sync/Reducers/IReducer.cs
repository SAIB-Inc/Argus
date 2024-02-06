using PallasDotnet.Models;

namespace Cardano.Sync.Reducers;

public interface IReducer
{
    Task RollForwardAsync(NextResponse response);
    Task RollBackwardAsync(NextResponse response);
}