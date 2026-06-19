using Argus.Sync.Providers;
using Argus.Sync.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.Extensions;

/// <summary>
/// Backend-agnostic registration for the Cardano indexer. A storage-backend package registers its own
/// <see cref="Argus.Sync.Reducers.IBlockUnitOfWorkFactory"/> and (optionally) <see cref="ISingleInstanceLock"/>,
/// then calls <see cref="AddCardanoIndexerCore"/> — for example <c>AddCardanoPostgresIndexer</c>
/// (from <c>Argus.Sync.EntityFramework</c>) or <c>AddCardanoMongoIndexer</c> (from <c>Argus.Sync.MongoDb</c>).
/// Register your reducers with <c>AddReducers</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the backend-agnostic indexer pieces: the chain-provider factory and the indexer worker as a
    /// hosted service. Each storage-backend entry point (Postgres, Mongo, …) calls this after registering its
    /// own <see cref="Argus.Sync.Reducers.IBlockUnitOfWorkFactory"/> and (optionally) <see cref="ISingleInstanceLock"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="chainProviderFactory">An optional custom chain provider factory; defaults to configuration-based if null.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCardanoIndexerCore(
        this IServiceCollection services,
        IChainProviderFactory? chainProviderFactory = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (chainProviderFactory != null)
        {
            _ = services.AddSingleton(chainProviderFactory);
        }
        else
        {
            _ = services.AddSingleton<IChainProviderFactory, ConfigurationChainProviderFactory>();
        }

        _ = services.AddHostedService<CardanoIndexWorker>();
        return services;
    }
}
