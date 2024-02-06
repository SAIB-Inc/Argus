using Cardano.Sync.Example.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cardano.Sync.Example.Data;

public class CardanoTestDbContextFactory : IDesignTimeDbContextFactory<CardanoTestDbContext>
{
    public CardanoTestDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<CardanoTestDbContext>();
        optionsBuilder.UseNpgsql(
            configuration!.GetConnectionString("CardanoContext"),
            x =>
            {
                x.MigrationsAssembly(typeof(CardanoTestDbContext).Assembly.FullName);
                x.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    configuration!.GetConnectionString("CardanoContextSchema")
                );
            }
        );

        return new CardanoTestDbContext(optionsBuilder.Options, configuration);
    }
}
