using Cardano.Sync.Example.Data;
using Cardano.Sync.Reducers;
using PallasDotnet.Models;

namespace Cardano.Sync.Example.Reducers;

[ReducerDepends(typeof(BlockReducer<CardanoTestDbContext>))]
public class TestReducer(
    ILogger<TestReducer> logger
) : IReducer
{
    public async Task RollForwardAsync(NextResponse response)
    {
        logger.LogInformation("Rolling forward {slot}", response.Block.Slot);
        await Task.CompletedTask;
    }

    public async Task RollBackwardAsync(NextResponse response)
    {
        logger.LogInformation("Rolling backward {slot}", response.Block.Slot);
        await Task.CompletedTask;
    }
}