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
    public DbSet<TestModel> TestModels => Set<TestModel>();
    public DbSet<TransactionOutput> TransactionOutputs => Set<TransactionOutput>();
    public DbSet<SundaeSwapTokenPrice> SundaeSwapTokenPrices => Set<SundaeSwapTokenPrice>();
    public DbSet<JpegPriceBySubject> JpegPriceBySubjects => Set<JpegPriceBySubject>();

    public DbSet<BalanceByAddress> BalanceByAddress=> Set<BalanceByAddress>();
    public DbSet<TxBySlot> TxBySlot=> Set<TxBySlot>();
    public DbSet<BlockBySlot> BlockBySlot=> Set<BlockBySlot>();
    public DbSet<SplashTokenPrice> SplashTokenPrice => Set<SplashTokenPrice>();

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

        modelBuilder.Entity<TransactionOutput>(entity =>
        {
            entity.HasKey(item => new { item.Id, item.Index });

            entity.HasIndex(item => item.Id);
            entity.HasIndex(item => item.Index);
            entity.HasIndex(item => item.Slot);
            entity.HasIndex(item => item.Address);
            entity.HasIndex(item => item.UtxoStatus);

            entity.OwnsOne(item => item.Datum);
            entity.Ignore(item => item.Amount);
        });
        
        modelBuilder.Entity<SundaeSwapTokenPrice>(entity =>
        {
            entity.HasKey(item => new { item.TokenXSubject, item.TokenYSubject, item.TxHash, item.TxIndex, item.Slot });

            entity.HasIndex(item => item.TokenXSubject);
            entity.HasIndex(item => item.TokenYSubject);
            entity.HasIndex(item => item.Slot);
            entity.HasIndex(item => item.TxHash);
        });

        modelBuilder.Entity<JpegPriceBySubject>(entity =>
        {
            entity.HasKey(e => new { e.Slot, e.TxHash, e.TxIndex, e.Subject, e.Status });

            entity.HasIndex(e => e.Slot);
            entity.HasIndex(e => e.Subject);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<TestModel>().HasKey(x => x.Slot);
        modelBuilder.Entity<ReducerState>().HasKey(x => x.Name);
        modelBuilder.Entity<BalanceByAddress>().HasKey(x => x.Address);
        modelBuilder.Entity<TxBySlot>().HasKey(x => new {x.BlockSlot, x.BlockHash, x.Transaction}); //moerror kung icomment
        modelBuilder.Entity<BlockBySlot>().HasKey(x => new {x.Slot, x.Hash});
        base.OnModelCreating(modelBuilder);
        
    }
}