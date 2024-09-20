using Argus.Sync.Data;
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
        services.AddDbContextFactory<T>(options =>
        {
            options
            .UseNpgsql(
                configuration!.GetConnectionString("CardanoContext"),
                    x =>
                    {
                        x.CommandTimeout(commandTimout);
                        x.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            configuration!.GetConnectionString("CardanoContextSchema")
                        );
                    }
                );
        });

        // Registering the hosted service
        services.AddHostedService<CardanoIndexWorker<T>>();

        // Return IServiceCollection to support method chaining
        return services;
    }
}
