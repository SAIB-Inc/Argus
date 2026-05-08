using System.Reflection;
using Argus.Sync.Data;
using Argus.Sync.Data.Stores;
using Argus.Sync.Providers;
using Argus.Sync.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.Extensions;

/// <summary>
/// Extension methods for registering the Cardano indexer services with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Cardano indexer services using the default configuration-based chain provider factory.
    /// </summary>
    /// <typeparam name="T">The database context type inheriting from <see cref="CardanoDbContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="commandTimout">The database command timeout in seconds.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCardanoIndexer<T>(this IServiceCollection services, IConfiguration configuration, int commandTimout = 60) where T : CardanoDbContext => AddCardanoIndexer<T>(services, configuration, commandTimout, null);

    /// <summary>
    /// Registers the Cardano indexer services with an optional custom chain provider factory for testing scenarios.
    /// </summary>
    /// <typeparam name="T">The database context type inheriting from <see cref="CardanoDbContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="commandTimout">The database command timeout in seconds.</param>
    /// <param name="chainProviderFactory">An optional custom chain provider factory; defaults to configuration-based if null.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddCardanoIndexer<T>(this IServiceCollection services, IConfiguration configuration, int commandTimout, IChainProviderFactory? chainProviderFactory) where T : CardanoDbContext
    {
        _ = services.AddDbContextFactory<T>(options =>
        {
            Assembly? contextAssembly = typeof(T).Assembly;
            _ = options
                .UseNpgsql(
                    configuration.GetConnectionString("CardanoContext"),
                        x =>
                        {
                            _ = x.MigrationsAssembly(contextAssembly!.FullName);
                            _ = x.CommandTimeout(commandTimout);
                            _ = x.MigrationsHistoryTable(
                                "__EFMigrationsHistory",
                                configuration!.GetConnectionString("CardanoContextSchema")
                            );
                            _ = x.EnableRetryOnFailure(
                                maxRetryCount: 5,
                                maxRetryDelay: TimeSpan.FromSeconds(30),
                                errorCodesToAdd: null
                            );
                        }
                );
        });

        // Register the default EF-backed reducer state store. Consumers using
        // a non-relational backend can replace this registration with their
        // own IReducerStateStore implementation after calling AddCardanoIndexer.
        _ = services.AddSingleton<IReducerStateStore, EfReducerStateStore<T>>();

        // Register chain provider factory - use provided factory or default to configuration-based
        if (chainProviderFactory != null)
        {
            _ = services.AddSingleton(chainProviderFactory);
        }
        else
        {
            _ = services.AddSingleton<IChainProviderFactory, ConfigurationChainProviderFactory>();
        }

        // Registering the hosted service
        _ = services.AddHostedService<CardanoIndexWorker<T>>();

        // Return IServiceCollection to support method chaining
        return services;
    }
}
