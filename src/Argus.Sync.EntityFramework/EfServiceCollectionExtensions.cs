using System.Reflection;
using Argus.Sync.Extensions;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Argus.Sync.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.EntityFramework;

/// <summary>
/// Dependency-injection entry point for running the Cardano indexer on the Entity Framework Core /
/// PostgreSQL backend. Call <see cref="AddCardanoPostgresIndexer{T}"/> with your
/// <see cref="CardanoDbContext"/>-derived context, then <c>AddReducers</c>. The Postgres counterpart of
/// <c>AddCardanoMongoIndexer</c>; both register their backend-specific storage seam and then share
/// <c>AddCardanoIndexerCore</c> from the core <c>Argus.Sync</c> library.
/// </summary>
public static class EfServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cardano indexer on the EF Core / PostgreSQL backend: a pooled DbContext factory, the EF
    /// unit-of-work factory (data + checkpoint storage), a Postgres single-instance advisory lock, the
    /// chain-provider factory, and the indexer worker as a hosted service.
    /// </summary>
    /// <remarks>
    /// <para><b>Transient-fault recovery.</b> Argus does not retry database faults in-process. EF Core's
    /// <c>EnableRetryOnFailure</c> execution strategy hard-throws on user-initiated transactions, and Argus
    /// opens one manual transaction per block-branch (it spans multiple pipeline tasks and captures raw
    /// <c>ExecuteUpdate</c>/SQL atomically) — a unit that cannot be expressed as a single retriable delegate.
    /// See the <c>UseNpgsql</c> note below. Recovery is fail-fast and crash-safe instead:</para>
    /// <list type="number">
    /// <item>A fault while processing a block rolls that block-branch's transaction back atomically — no partial
    /// writes (tracked rows, raw SQL, and the reducer-state checkpoint all roll back together).</item>
    /// <item>The fault propagates out of <see cref="CardanoIndexWorker"/>'s execute loop, which stops the host
    /// (the default <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> behavior).</item>
    /// <item>An external supervisor (systemd, Kubernetes, Docker <c>restart:</c>) restarts the process, which
    /// resumes from the last atomically-committed checkpoint and replays the failed block.</item>
    /// </list>
    /// <para>Because data and checkpoint commit in one transaction, replay is at-least-once with idempotent
    /// re-processing — no data loss or corruption. Configure your host with a restart policy.</para>
    /// </remarks>
    /// <typeparam name="T">The database context type inheriting from <see cref="CardanoDbContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="commandTimout">The database command timeout in seconds.</param>
    /// <param name="chainProviderFactory">An optional custom chain provider factory; defaults to configuration-based if null.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCardanoPostgresIndexer<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        int commandTimout = 60,
        IChainProviderFactory? chainProviderFactory = null) where T : CardanoDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services.AddDbContextFactory<T>(options =>
        {
            Assembly? contextAssembly = typeof(T).Assembly;

            // EnableRetryOnFailure is intentionally NOT configured: EF's retrying execution strategy throws on
            // user-initiated transactions not wrapped in CreateExecutionStrategy().ExecuteAsync(...), and the
            // per-block-branch transaction spans multiple pipeline tasks (and captures raw ExecuteUpdate/SQL),
            // so it cannot be a single retriable delegate. Transient faults are recovered out-of-process via
            // fail-fast + restart + checkpoint-resume — see the remarks above.
            _ = options.UseNpgsql(
                configuration.GetConnectionString("CardanoContext"),
                x =>
                {
                    _ = x.MigrationsAssembly(contextAssembly!.FullName);
                    _ = x.CommandTimeout(commandTimout);
                    _ = x.MigrationsHistoryTable("__EFMigrationsHistory", configuration!.GetConnectionString("CardanoContextSchema"));
                });
        });

        // EF unit-of-work factory — the storage-backend seam: transactional per-branch units + checkpoint reads.
        _ = services.AddSingleton<IBlockUnitOfWorkFactory>(sp =>
            new EfBlockUnitOfWorkFactory<T>(sp.GetRequiredService<IDbContextFactory<T>>(), configuration));

        // Postgres single-instance guard (advisory lock): ONE shared singleton, exposed both as
        // ISingleInstanceLock (the indexer's gate) and as a hosted service (runs the acquire/hold/release loop).
        // The factory indirection keeps both roles on the SAME instance. Opt out via Sync:SingleInstanceLock:Enabled=false.
        if (configuration.GetValue("Sync:SingleInstanceLock:Enabled", true))
        {
            _ = services.AddSingleton<PostgresSingleInstanceLockWorker>();
            _ = services.AddSingleton<ISingleInstanceLock>(sp => sp.GetRequiredService<PostgresSingleInstanceLockWorker>());
            _ = services.AddHostedService(sp => sp.GetRequiredService<PostgresSingleInstanceLockWorker>());
        }

        return services.AddCardanoIndexerCore(chainProviderFactory);
    }
}
