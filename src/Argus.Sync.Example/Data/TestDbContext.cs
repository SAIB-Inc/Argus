using Argus.Sync.Data;
using Argus.Sync.Example.Models;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Data;

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
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        _ = modelBuilder.Entity<BlockTest>(entity =>
        {
            _ = entity.HasKey(b => new { b.Hash, b.Slot });
        });

        _ = modelBuilder.Entity<TransactionTest>(entity =>
        {
            _ = entity.HasKey(e => new { e.TxHash, e.TxIndex });
        });
    }
}