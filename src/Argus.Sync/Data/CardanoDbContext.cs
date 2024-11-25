using System.Reflection;
using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public interface ICardanoDbContext
{
    DbSet<ReducerState> ReducerStates { get; }
}

public class CardanoDbContext(
    DbContextOptions Options,
    IConfiguration Configuration
) : DbContext(Options), ICardanoDbContext
{
    public DbSet<ReducerState> ReducerStates => Set<ReducerState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (PropertyInfo property in GetType().GetProperties())
        {
            if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            {
                Type entityType = property.PropertyType.GetGenericArguments()[0];
                modelBuilder.Ignore(entityType);
            }
        }

        modelBuilder.Entity<ReducerState>(entity =>
        {
            entity.HasKey(e => e.Name);
        });

        modelBuilder.HasDefaultSchema(Configuration.GetConnectionString("CardanoContextSchema"));
        base.OnModelCreating(modelBuilder);
    }
}