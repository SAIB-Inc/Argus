using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Data.Stores;

/// <summary>
/// Factory that produces a fresh <see cref="EfBlockUnitOfWork{TContext}"/> per
/// branch per block, each owning a freshly created <see cref="DbContext"/>
/// instance from the consumer's <see cref="IDbContextFactory{TContext}"/>.
/// </summary>
public sealed class EfBlockUnitOfWorkFactory<TContext>(IDbContextFactory<TContext> dbContextFactory)
    : IBlockUnitOfWorkFactory
    where TContext : CardanoDbContext
{
    /// <inheritdoc />
    public IBlockUnitOfWork Create()
    {
        TContext dbContext = dbContextFactory.CreateDbContext();
        return new EfBlockUnitOfWork<TContext>(dbContext);
    }
}
