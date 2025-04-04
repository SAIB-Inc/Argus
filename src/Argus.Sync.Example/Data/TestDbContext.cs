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
    }
}