using System.Reflection;
using Argus.Sync.Data;
using Argus.Sync.Providers;
using Argus.Sync.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Argus.Sync.Extensions;

// Extension method class must be static
public static class ServiceCollectionExtensions
{
    // Extension method to encapsulate the Cardano indexer service registrations
    public static IServiceCollection AddCardanoIndexer<T>(this IServiceCollection services, IConfiguration configuration, int commandTimout = 60) where T : CardanoDbContext
    {
        return AddCardanoIndexer<T>(services, configuration, commandTimout, null);
    }

    // Extension method with custom chain provider factory for testing scenarios
    public static IServiceCollection AddCardanoIndexer<T>(this IServiceCollection services, IConfiguration configuration, int commandTimout, IChainProviderFactory? chainProviderFactory) where T : CardanoDbContext
    {
        services.AddDbContextFactory<T>(options =>
        {
            Assembly? contextAssembly = typeof(T).Assembly;
            options
                .UseNpgsql(
                    configuration.GetConnectionString("CardanoContext"),
                        x =>
                        {
                            x.MigrationsAssembly(contextAssembly!.FullName);
                            x.CommandTimeout(commandTimout);
                            x.MigrationsHistoryTable(
                                "__EFMigrationsHistory",
                                configuration!.GetConnectionString("CardanoContextSchema")
                            );
                        }
                );
        });

        // Register chain provider factory - use provided factory or default to configuration-based
        if (chainProviderFactory != null)
        {
            services.AddSingleton<IChainProviderFactory>(chainProviderFactory);
        }
        else
        {
            services.AddSingleton<IChainProviderFactory, ConfigurationChainProviderFactory>();
        }

        // Registering the hosted service
        services.AddHostedService<CardanoIndexWorker<T>>();

        // Return IServiceCollection to support method chaining
        return services;
    }
}
