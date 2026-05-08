using Argus.Sync.Example.Data;
using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

[DependsOn(typeof(BlockTestReducer))]
public class DependentTransactionReducer : IReducer
{
    public Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
    {
        // Demonstration only — read what *would* be rolled back. The framework
        // owns the actual rollback semantics; this reducer doesn't write here.
        return Task.CompletedTask;
    }

    public async Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(uow);
        TestDbContext dbContext = uow.GetStorage<TestDbContext>();
        ulong slot = block.Header().HeaderBody().Slot();

        // Per-branch UoW: BlockTestReducer's Add() above is visible to this
        // dependent via the change-tracker's Local view, even though the data
        // hasn't been committed to the DB yet. No DB round-trip needed.
        bool blockExists = dbContext.BlockTests.Local.Any(b => b.Slot == slot);
        if (!blockExists)
        {
            throw new InvalidOperationException($"Block at slot {slot} not found - dependency not satisfied!");
        }

        // Count is from the DB (committed prior blocks) plus Local (pending this block).
        // For a real reducer, this is where read-modify-write logic would go.
        _ = await dbContext.TransactionTests.Where(t => t.Slot == slot).CountAsync(ct).ConfigureAwait(false);
    }
}
