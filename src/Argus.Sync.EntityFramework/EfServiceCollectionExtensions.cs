using Argus.Sync.Extensions;
using Argus.Sync.Providers;
using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.EntityFramework;

/// <summary>
/// Dependency-injection entry point for running the Cardano indexer on a provider-neutral Entity
/// Framework Core backend. A provider package supplies the concrete EF provider via
/// <c>configureProvider</c> and registers its own single-instance lock, then calls
/// <see cref="AddCardanoEntityFrameworkIndexer{T}"/> — for example <c>AddCardanoPostgresIndexer</c>
/// from <c>Argus.Sync.EntityFramework.Postgres</c>. Both this and the Mongo backend register their
/// backend-specific storage seam and then share <c>AddCardanoIndexerCore</c> from the core
/// <c>Argus.Sync</c> library.
/// </summary>
public static class EfServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cardano indexer on a provider-neutral EF Core backend: a DbContext factory (configured
    /// by <paramref name="configureProvider"/>), the EF unit-of-work factory (data + checkpoint storage), the
    /// chain-provider factory, and the indexer worker as a hosted service. It does <b>not</b> register a
    /// single-instance lock — a provider package adds the lock appropriate to its database (e.g. a Postgres
    /// session advisory lock) before calling this.
    /// </summary>
    /// <remarks>
    /// <para><b>Transient-fault recovery.</b> Argus does not retry database faults in-process. EF Core's
    /// <c>EnableRetryOnFailure</c> execution strategy hard-throws on user-initiated transactions, and Argus
    /// opens one manual transaction per block-branch (it spans multiple pipeline tasks and captures raw
    /// <c>ExecuteUpdate</c>/SQL atomically) — a unit that cannot be expressed as a single retriable delegate.
    /// Provider registrations therefore do not enable a retrying execution strategy. Recovery is fail-fast and
    /// crash-safe instead:</para>
    /// <list type="number">
    /// <item>A fault while processing a block rolls that block-branch's transaction back atomically — no partial
    /// writes (tracked rows, raw SQL, and the reducer-state checkpoint all roll back together).</item>
    /// <item>The fault propagates out of <see cref="Workers.CardanoIndexWorker"/>'s execute loop,
    /// which stops the host (the default <see cref="Microsoft.Extensions.Hosting.BackgroundService"/> behavior).</item>
    /// <item>An external supervisor (systemd, Kubernetes, Docker <c>restart:</c>) restarts the process, which
    /// resumes from the last atomically-committed checkpoint and replays the failed block.</item>
    /// </list>
    /// <para>Because data and checkpoint commit in one transaction, replay is at-least-once with idempotent
    /// re-processing — no data loss or corruption. Configure your host with a restart policy.</para>
    /// </remarks>
    /// <typeparam name="T">The database context type inheriting from <see cref="CardanoDbContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="configureProvider">Configures the EF Core provider on the DbContext options (e.g. <c>o =&gt; o.UseNpgsql(...)</c>).</param>
    /// <param name="chainProviderFactory">An optional custom chain provider factory; defaults to configuration-based if null.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCardanoEntityFrameworkIndexer<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder> configureProvider,
        IChainProviderFactory? chainProviderFactory = null) where T : CardanoDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configureProvider);

        _ = services.AddDbContextFactory<T>(configureProvider);

        // EF unit-of-work factory — the storage-backend seam: transactional per-branch units + checkpoint reads.
        _ = services.AddSingleton<IBlockUnitOfWorkFactory>(sp =>
            new EfBlockUnitOfWorkFactory<T>(sp.GetRequiredService<IDbContextFactory<T>>(), configuration));

        return services.AddCardanoIndexerCore(chainProviderFactory);
    }
}
