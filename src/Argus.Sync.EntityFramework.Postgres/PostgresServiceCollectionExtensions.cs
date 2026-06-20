using System.Reflection;
using Argus.Sync.Providers;
using Argus.Sync.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.EntityFramework.Postgres;

/// <summary>
/// Dependency-injection entry point for running the Cardano indexer on the PostgreSQL / Npgsql backend.
/// Call <see cref="AddCardanoPostgresIndexer{T}"/> with your <see cref="CardanoDbContext"/>-derived context,
/// then <c>AddReducers</c>. This is a thin Npgsql wrapper over the provider-neutral
/// <see cref="EfServiceCollectionExtensions.AddCardanoEntityFrameworkIndexer{T}"/>: it supplies the
/// <c>UseNpgsql</c> provider and registers a Postgres session-advisory single-instance lock.
/// </summary>
public static class PostgresServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cardano indexer on the EF Core / PostgreSQL backend: an Npgsql-backed DbContext factory,
    /// the EF unit-of-work factory (data + checkpoint storage), a Postgres single-instance advisory lock, the
    /// chain-provider factory, and the indexer worker as a hosted service.
    /// </summary>
    /// <typeparam name="T">The database context type inheriting from <see cref="CardanoDbContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="commandTimeout">The database command timeout in seconds.</param>
    /// <param name="chainProviderFactory">An optional custom chain provider factory; defaults to configuration-based if null.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCardanoPostgresIndexer<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        int commandTimeout = 60,
        IChainProviderFactory? chainProviderFactory = null) where T : CardanoDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Postgres single-instance guard (advisory lock): ONE shared singleton, exposed both as
        // ISingleInstanceLock (the indexer's gate) and as a hosted service (runs the acquire/hold/release loop).
        // The factory indirection keeps both roles on the SAME instance. Opt out via Sync:SingleInstanceLock:Enabled=false.
        if (configuration.GetValue("Sync:SingleInstanceLock:Enabled", true))
        {
            _ = services.AddSingleton<PostgresSingleInstanceLock>();
            _ = services.AddSingleton<ISingleInstanceLock>(sp => sp.GetRequiredService<PostgresSingleInstanceLock>());
            _ = services.AddHostedService(sp => sp.GetRequiredService<PostgresSingleInstanceLock>());
        }

        return services.AddCardanoEntityFrameworkIndexer<T>(
            configuration,
            options =>
            {
                Assembly? contextAssembly = typeof(T).Assembly;

                // EnableRetryOnFailure is intentionally NOT configured: EF's retrying execution strategy throws on
                // user-initiated transactions not wrapped in CreateExecutionStrategy().ExecuteAsync(...), and the
                // per-block-branch transaction spans multiple pipeline tasks (and captures raw ExecuteUpdate/SQL),
                // so it cannot be a single retriable delegate. Transient faults are recovered out-of-process via
                // fail-fast + restart + checkpoint-resume — see AddCardanoEntityFrameworkIndexer's remarks.
                _ = options.UseNpgsql(
                    configuration.GetConnectionString("CardanoContext"),
                    x =>
                    {
                        _ = x.MigrationsAssembly(contextAssembly!.FullName);
                        _ = x.CommandTimeout(commandTimeout);
                        _ = x.MigrationsHistoryTable("__EFMigrationsHistory", configuration.GetConnectionString("CardanoContextSchema"));
                    });
            },
            chainProviderFactory);
    }
}
