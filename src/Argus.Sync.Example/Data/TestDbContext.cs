using Argus.Sync.Data;
using Argus.Sync.Example.Models;
using Microsoft.EntityFrameworkCore;

public class TestDbContext
(
    DbContextOptions<TestDbContext> options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration)
{
    public DbSet<BlockTest> BlockTests => Set<BlockTest>();
    public DbSet<TransactionTest> TransactionTests => Set<TransactionTest>();
    public DbSet<TxBySlot> TxBySlot => Set<TxBySlot>();
    public DbSet<OutputBySlot> OutputBySlot => Set<OutputBySlot>();
    public DbSet<PriceByToken> PricesByToken => Set<PriceByToken>();
    public DbSet<OrderBySlot> OrdersBySlot => Set<OrderBySlot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BlockTest>(entity =>
        {
            entity.HasKey(e => e.BlockHash);
        });

        modelBuilder.Entity<TransactionTest>(entity =>
        {
            entity.HasKey(e => new { e.TxHash, e.TxIndex });
        });

        modelBuilder.Entity<TxBySlot>(e =>
        {
            e.HasKey(e => new { e.TxHash, e.Index });
        });

        modelBuilder.Entity<OutputBySlot>(e =>
        {
            e.HasKey(e => new { e.TxHash, e.TxIndex });
        });

        modelBuilder.Entity<PriceByToken>(e =>
        {
            e.HasKey(e => new { e.OutRef, e.Slot, e.TokenXSubject, e.TokenYSubject, e.PlatformType });
        });
    }
}