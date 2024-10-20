using System.Reflection;
using Argus.Sync.Data.Models;
using Argus.Sync.Data.Models.SundaeSwap;
using Argus.Sync.Data.Models.Splash;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public class CardanoDbContext(
    DbContextOptions options,
    IConfiguration configuration
) : DbContext(options), IReducerModel
{
    private readonly IConfiguration _configuration = configuration;
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

        modelBuilder.HasDefaultSchema(_configuration.GetConnectionString("CardanoContextSchema"));
        base.OnModelCreating(modelBuilder);
    }
}