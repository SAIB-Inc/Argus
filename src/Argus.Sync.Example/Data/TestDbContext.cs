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
    public DbSet<OrderBySlot> OrdersBySlot => Set<OrderBySlot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OrderBySlot>(entity =>
        {
            entity.HasKey(e => new { e.TxHash, e.Index });
        });
    }
}