using System.Reflection;
using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

/// <summary>
/// Interface for the Cardano database context providing access to reducer states.
/// </summary>
public interface ICardanoDbContext
{
    /// <summary>Gets the set of reducer states.</summary>
    DbSet<ReducerState> ReducerStates { get; }
}

/// <summary>
/// Base database context for Cardano blockchain data, managing reducer state and entity configuration.
/// </summary>
/// <param name="Options">The database context options.</param>
/// <param name="Configuration">The application configuration.</param>
public class CardanoDbContext(
    DbContextOptions Options,
    IConfiguration Configuration
) : DbContext(Options), ICardanoDbContext
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
