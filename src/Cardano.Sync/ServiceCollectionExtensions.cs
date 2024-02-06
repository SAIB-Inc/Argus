using System.Reflection;
using Cardano.Sync.Data;
using Cardano.Sync.Reducers;
using Cardano.Sync.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cardano.Sync;

// Extension method class must be static
public static class ServiceCollectionExtensions
{
    // Extension method to encapsulate the Cardano indexer service registrations
    public static IServiceCollection AddCardanoIndexer<T>(this IServiceCollection services, IConfiguration configuration, Assembly migrationAssembly) where T : CardanoDbContext
    {
        services.AddDbContextFactory<T>(options =>
        {
            options
            .UseNpgsql(
                configuration!.GetConnectionString("CardanoContext"),
                    x =>
                    {
                        x.MigrationsAssembly(migrationAssembly.FullName);
                        x.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            configuration!.GetConnectionString("CardanoContextSchema")
                        );
                    }
                );
        });
        // Registering required services as singletons
        services.AddSingleton<IBlockReducer, BlockReducer<T>>();
        services.AddSingleton<ICoreReducer, TransactionOutputReducer<T>>();

        // Registering the hosted service
        services.AddHostedService<CardanoIndexWorker<T>>();

        // Return IServiceCollection to support method chaining
        return services;
    }
}
