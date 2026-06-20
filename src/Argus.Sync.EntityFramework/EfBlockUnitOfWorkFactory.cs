using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.EntityFramework;

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
        IDbContextTransaction transaction =
            await dbContext.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        return new EfBlockUnitOfWork<TContext>(dbContext, transaction, _rollbackBuffer);
    }

    /// <inheritdoc />
    public async Task<ReducerState?> GetReducerStateAsync(string reducerName, CancellationToken ct = default)
    {
        await using TContext dbContext = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await dbContext.ReducerStates
            .AsNoTracking()
            .Where(s => s.Name == reducerName)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }
}
