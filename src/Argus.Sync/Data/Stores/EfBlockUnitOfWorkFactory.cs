using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Data.Stores;

/// <summary>
/// Factory that produces a fresh <see cref="EfBlockUnitOfWork{TContext}"/> per
/// branch per block, each owning a freshly created <see cref="DbContext"/>
/// instance and open transaction from the consumer's
/// <see cref="IDbContextFactory{TContext}"/>.
/// </summary>
public sealed class EfBlockUnitOfWorkFactory<TContext>(IDbContextFactory<TContext> dbContextFactory)
    : IBlockUnitOfWorkFactory
    where TContext : CardanoDbContext
{
    /// <inheritdoc />
    public async Task<IBlockUnitOfWork> CreateAsync(CancellationToken ct = default)
    {
        TContext dbContext = await dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        return new EfBlockUnitOfWork<TContext>(dbContext, transaction);
    }
}
