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
    public DbSet<TxBySlot> TxsBySlot => Set<TxBySlot>();
    public DbSet<OutputBySlot> OutputBySlot => Set<OutputBySlot>();
    public DbSet<PriceByToken> PricesByToken => Set<PriceByToken>();
    public DbSet<OrderBySlot> OrdersBySlot => Set<OrderBySlot>();
    public DbSet<OwnerBySlot> AssetOwnerBySlot => Set<OwnerBySlot>();
    public DbSet<Royalty> Royalties => Set<Royalty>();
    public DbSet<UtxoByAddress> UtxosByAddress => Set<UtxoByAddress>();

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

        modelBuilder.Entity<OrderBySlot>(entity =>
        {
            entity.HasKey(e => new { e.TxHash, e.TxIndex });
        });

        modelBuilder.Entity<BalanceByAddress>(entity =>
        {
            entity.HasKey(e => e.Address);
        });

        modelBuilder.Entity<OwnerBySlot>(entity =>
        {
            entity.HasKey(e => new { e.Address, e.Subject, e.Slot, e.OutRef });
        });

        modelBuilder.Entity<Royalty>(entity =>
        {
            entity.HasKey(e => e.PolicyId);
        });

        modelBuilder.Entity<UtxoByAddress>(entity =>
        {
            entity.HasKey(e => new { e.Address, e.Slot, e.TxHash, e.TxIndex, e.Status });
        });
    }
}