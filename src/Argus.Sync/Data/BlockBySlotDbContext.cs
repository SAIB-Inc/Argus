using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public interface IBlockBySlotDbContext
{
    DbSet<BlockBySlot> BlockBySlot { get; }
}

public class BlockBySlotDbContext
(
    DbContextOptions<BlockBySlotDbContext> options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration), IBlockBySlotDbContext
{
    public DbSet<BlockBySlot> BlockBySlot => Set<BlockBySlot>();

    override protected void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BlockBySlot>(entity =>
        {
            entity.HasKey(e => new { e.Slot, e.Hash });
        });
    }
}
