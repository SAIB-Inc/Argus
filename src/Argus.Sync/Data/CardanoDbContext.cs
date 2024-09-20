using System.Reflection;
using Argus.Sync.Data.Models;
using Argus.Sync.Reducers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace Argus.Sync.Data;


public class CardanoDbContext(
    DbContextOptions options,
    IConfiguration configuration
) : DbContext(options)
{
    private readonly IConfiguration _configuration = configuration;
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<TransactionOutput> TransactionOutputs => Set<TransactionOutput>();
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

        modelBuilder.Entity<ReducerState>().HasKey(x => x.Name);

        base.OnModelCreating(modelBuilder);
    }
}