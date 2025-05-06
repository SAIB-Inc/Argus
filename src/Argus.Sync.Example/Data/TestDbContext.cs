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
    public DbSet<OutputBySlot> OutputsBySlot => Set<OutputBySlot>();
    public DbSet<UtxoByAddress> UtxosByAddress => Set<UtxoByAddress>();
    public DbSet<SundaePriceByToken> SundaePricesByToken => Set<SundaePriceByToken>();

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

        modelBuilder.Entity<OutputBySlot>(entity =>
        {
            entity.HasKey(e => new { e.TxHash, e.TxIndex });
        });

        modelBuilder.Entity<UtxoByAddress>(entity =>
        {
            entity.HasKey(e => new { e.TxHash, e.TxIndex, e.Slot, e.Address });
        });
    }
}