using Argus.Sync.Data;
using Argus.Sync.Example.Models;
using Microsoft.EntityFrameworkCore;

public class TestDbContext
(
    DbContextOptions<TestDbContext> options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration)
{
    public DbSet<UtxosByAddress> UtxosByAddress => Set<UtxosByAddress>();
    public DbSet<TestDependency> TestDependencies => Set<TestDependency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UtxosByAddress>(entity =>
        {
            entity.HasKey(e => new { e.TxHash, e.TxIndex });
        });

        modelBuilder.Entity<TestDependency>(entity =>
        {
            entity.HasKey(e => e.TxHash);
        });
    }
}