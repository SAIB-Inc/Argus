using Argus.Sync.EntityFramework;
using Argus.Sync.EntityFramework.Postgres;
using Argus.Sync.Example.Data;
using Argus.Sync.MongoDb;
using Argus.Sync.Reducers;
using Argus.Sync.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Argus.Sync.Tests.EndToEnd;

/// <summary>
/// Proves the two backend registration entry points wire a correct DI graph. Each registers its own
/// storage seam (<see cref="IBlockUnitOfWorkFactory"/>) and single-instance lock, exposes that lock as
/// ONE shared singleton across both roles (the indexer's gate <see cref="ISingleInstanceLock"/> and the
/// hosted service that runs the acquire/hold loop), and adds the shared <see cref="CardanoIndexWorker"/>
/// as a hosted service via <c>AddCardanoIndexerCore</c>. Pure DI assertions — no node and no database
/// connection (the Mongo client and EF context factory both construct lazily).
/// </summary>
public sealed class IndexerRegistrationTest
{
    [Fact]
    public void AddCardanoMongoIndexer_WiresMongoStorageLockAndWorker()
    {
        IConfiguration config = BuildConfig();
        ServiceCollection services = NewServices(config);
        _ = services.AddCardanoMongoIndexer(config);

        using ServiceProvider provider = services.BuildServiceProvider();

        // The storage seam is the Mongo unit-of-work factory.
        _ = Assert.IsType<MongoBlockUnitOfWorkFactory>(provider.GetRequiredService<IBlockUnitOfWorkFactory>());

        // The lock is the Mongo lease lock, and the SAME instance serves both roles (gate + hosted service).
        ISingleInstanceLock gate = provider.GetRequiredService<ISingleInstanceLock>();
        MongoSingleInstanceLock concrete = provider.GetRequiredService<MongoSingleInstanceLock>();
        Assert.Same(concrete, gate);

        AssertWorkerHosted(services);
    }

    [Fact]
    public void AddCardanoPostgresIndexer_WiresEfStorageLockAndWorker()
    {
        IConfiguration config = BuildConfig();
        ServiceCollection services = NewServices(config);
        _ = services.AddCardanoPostgresIndexer<TestDbContext>(config);

        using ServiceProvider provider = services.BuildServiceProvider();

        // The storage seam is the EF unit-of-work factory over the chosen context.
        _ = Assert.IsType<EfBlockUnitOfWorkFactory<TestDbContext>>(provider.GetRequiredService<IBlockUnitOfWorkFactory>());

        // The lock is the Postgres advisory lock, shared across both roles.
        ISingleInstanceLock gate = provider.GetRequiredService<ISingleInstanceLock>();
        PostgresSingleInstanceLock concrete = provider.GetRequiredService<PostgresSingleInstanceLock>();
        Assert.Same(concrete, gate);

        AssertWorkerHosted(services);
    }

    private static void AssertWorkerHosted(IServiceCollection services)
    {
        bool workerHosted = services.Any(d =>
            d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(CardanoIndexWorker));
        Assert.True(workerHosted, "CardanoIndexWorker should be registered as a hosted service.");
    }

    private static ServiceCollection NewServices(IConfiguration configuration)
    {
        ServiceCollection services = new();
        _ = services.AddLogging();
        _ = services.AddSingleton(configuration);
        _ = services.AddSingleton<IHostApplicationLifetime>(new FakeLifetime());
        return services;
    }

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:CardanoMongo"] = "mongodb://localhost:27017/?directConnection=true",
            ["ConnectionStrings:CardanoContext"] = "Host=localhost;Database=argus;Username=postgres;Password=postgres;Port=4321",
            ["ConnectionStrings:CardanoContextSchema"] = "argus_test",
            ["Mongo:Database"] = "argus_test",
        }).Build();

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
