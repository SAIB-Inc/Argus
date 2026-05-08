using Argus.Sync.Example.Data;
using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

[DependsOn(typeof(DependentTransactionReducer))]
public class ChainedDependentReducer : IReducer
{
    public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public async Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(uow);
        TestDbContext dbContext = uow.GetStorage<TestDbContext>();
        ulong slot = block.Header().HeaderBody().Slot();

        // Both upstream reducers (BlockTestReducer, DependentTransactionReducer)
        // have run inside this same UoW; their pending writes are visible via
        // Local. This demonstrates the chained dependency:
        //   BlockTestReducer -> DependentTransactionReducer -> this
        bool blockExists = dbContext.BlockTests.Local.Any(b => b.Slot == slot);
        if (!blockExists)
        {
            throw new InvalidOperationException($"Block at slot {slot} not found - BlockTestReducer dependency not satisfied!");
        }

        _ = await dbContext.TransactionTests.Where(t => t.Slot == slot).CountAsync(ct).ConfigureAwait(false);
    }
}
