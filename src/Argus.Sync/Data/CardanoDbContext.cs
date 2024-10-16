using System.Reflection;
using Argus.Sync.Data.Models;
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
    public DbSet<TestModel> TestModels => Set<TestModel>();

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

        modelBuilder.Entity<ReducerState>().HasKey(x => x.Name);
        modelBuilder.Entity<TestModel>().HasKey(x => x.Slot);

        base.OnModelCreating(modelBuilder);
    }
}