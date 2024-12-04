using Argus.Sync.Data.Models;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

[ReducerDepends(typeof(UtxosByAddressReducer))]
public class TestDependencyReducer(IDbContextFactory<TestDbContext> dbContextFactory) : IReducer<TestDependency>
{
    public async Task<ulong?> QueryTip()
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.TestDependencies.Select(td => td.Slot).FirstOrDefaultAsync();
    }

    public async Task RollBackwardAsync(ulong slot)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var testDependenciesToRemove = dbContext.TestDependencies.Where(td => td.Slot > slot);
        dbContext.TestDependencies.RemoveRange(testDependenciesToRemove);
        await dbContext.SaveChangesAsync();
        await dbContext.DisposeAsync();
    }

    public async Task RollForwardAsync(Chrysalis.Cardano.Core.Block block)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var testDependencies = block.TransactionBodies()
            .Select(tx => new TestDependency(tx.Id(), block.Slot()))
            .Where(td => td.TxHash != null);
        dbContext.TestDependencies.AddRange(testDependencies);
        await dbContext.SaveChangesAsync();
    }
}