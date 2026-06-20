using System.Reflection;
using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.EntityFramework;

/// <summary>
/// Base database context for EF Core consumers. Inherit from this to colocate
/// your reducer-data tables with the framework's <see cref="ReducerState"/>
/// table in a single Postgres schema. Non-EF consumers do not need this type;
/// implement <see cref="Reducers.IBlockUnitOfWorkFactory"/> directly instead.
/// </summary>
/// <param name="Options">The database context options.</param>
/// <param name="Configuration">The application configuration.</param>
public class CardanoDbContext(
    DbContextOptions Options,
    IConfiguration Configuration
) : DbContext(Options)
{
    /// <summary>Gets the set of reducer states.</summary>
    public DbSet<ReducerState> ReducerStates => Set<ReducerState>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (PropertyInfo property in GetType().GetProperties())
        {
            if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            {
                Type entityType = property.PropertyType.GetGenericArguments()[0];
                _ = modelBuilder.Ignore(entityType);
            }
        }

        _ = modelBuilder.Entity<ReducerState>(e =>
        {
            _ = e.HasKey(e => e.Name);
        });

        _ = modelBuilder.HasDefaultSchema(Configuration.GetConnectionString("CardanoContextSchema"));
        base.OnModelCreating(modelBuilder);
    }
}
