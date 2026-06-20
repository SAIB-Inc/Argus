using Argus.Sync.Data;
using Argus.Sync.Extensions;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Argus.Sync.MongoDb;

/// <summary>
/// Dependency-injection entry point for running the Cardano indexer on the MongoDB backend. The Mongo
/// counterpart of <c>AddCardanoPostgresIndexer</c>; both register their backend-specific storage seam and
/// then share <c>AddCardanoIndexerCore</c> (the chain-provider factory and the indexer worker).
/// </summary>
public static class MongoServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cardano indexer on the MongoDB backend: a singleton <see cref="IMongoClient"/>, the
    /// Mongo unit-of-work factory (data + checkpoint storage), a Mongo single-instance lease lock, the
    /// chain-provider factory, and the indexer worker as a hosted service. Then call <c>AddReducers</c> to
    /// register your reducers.
    /// </summary>
    /// <remarks>
    /// The connection string must target a replica set (or sharded cluster) so MongoDB multi-document
    /// transactions are available — Argus writes each block-branch's data and its reducer-state checkpoint
    /// in one transaction. Transient-fault recovery is fail-fast + restart + checkpoint-resume, identical to
    /// the Postgres backend.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration. Reads <c>ConnectionStrings:CardanoMongo</c>
    /// (required) and <c>Mongo:Database</c> (optional, default <c>argus</c>).</param>
    /// <param name="chainProviderFactory">An optional custom chain provider factory; defaults to configuration-based if null.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCardanoMongoIndexer(
        this IServiceCollection services,
        IConfiguration configuration,
        IChainProviderFactory? chainProviderFactory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("CardanoMongo")
            ?? throw new InvalidOperationException("ConnectionStrings:CardanoMongo is required for the Mongo indexer.");
        string databaseName = configuration["Mongo:Database"] ?? "argus";
        int rollbackBuffer = configuration.GetValue("CardanoNodeConnection:RollbackBuffer", ReducerStateCheckpointWindow.DefaultMaxCount);

        // One shared Mongo client (it manages its own connection pool internally).
        _ = services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));

        // Mongo unit-of-work factory — the storage-backend seam: transactional per-branch units + checkpoint reads.
        _ = services.AddSingleton<IBlockUnitOfWorkFactory>(sp =>
            new MongoBlockUnitOfWorkFactory(sp.GetRequiredService<IMongoClient>(), databaseName, rollbackBuffer));

        // Mongo single-instance guard (lease lock): ONE shared singleton, exposed both as ISingleInstanceLock
        // (the indexer's gate) and as a hosted service (runs the acquire/renew/release loop). The factory
        // indirection keeps both roles on the SAME instance. Opt out via Sync:SingleInstanceLock:Enabled=false.
        if (configuration.GetValue("Sync:SingleInstanceLock:Enabled", true))
        {
            _ = services.AddSingleton<MongoSingleInstanceLock>();
            _ = services.AddSingleton<ISingleInstanceLock>(sp => sp.GetRequiredService<MongoSingleInstanceLock>());
            _ = services.AddHostedService(sp => sp.GetRequiredService<MongoSingleInstanceLock>());
        }

        return services.AddCardanoIndexerCore(chainProviderFactory);
    }
}
