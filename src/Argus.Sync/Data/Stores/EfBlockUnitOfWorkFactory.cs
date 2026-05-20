using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data.Stores;

/// <summary>
/// Factory that produces a fresh <see cref="EfBlockUnitOfWork{TContext}"/> per
/// branch per block, each owning a freshly created <see cref="DbContext"/>
/// instance and open transaction from the consumer's
/// <see cref="IDbContextFactory{TContext}"/>.
/// </summary>
public sealed class EfBlockUnitOfWorkFactory<TContext> : IBlockUnitOfWorkFactory
    where TContext : CardanoDbContext
{
    private readonly IDbContextFactory<TContext> _dbContextFactory;
    private readonly int _rollbackBuffer;

    /// <summary>
    /// Creates a factory with an optional configuration source for
    /// <c>CardanoNodeConnection:RollbackBuffer</c>.
    /// </summary>
    public EfBlockUnitOfWorkFactory(
        IDbContextFactory<TContext> dbContextFactory,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);

        _dbContextFactory = dbContextFactory;
        _rollbackBuffer = Math.Max(1, configuration?.GetValue(
            "CardanoNodeConnection:RollbackBuffer",
            ReducerStateCheckpointWindow.DefaultMaxCount) ?? ReducerStateCheckpointWindow.DefaultMaxCount);
    }

    /// <inheritdoc />
    public async Task<IBlockUnitOfWork> CreateAsync(CancellationToken ct = default)
    {
        TContext dbContext = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        return new EfBlockUnitOfWork<TContext>(dbContext, transaction, _rollbackBuffer);
    }
}
