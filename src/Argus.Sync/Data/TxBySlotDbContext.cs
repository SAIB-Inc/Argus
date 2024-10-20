using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public interface ITxBySlotDbContext
{
    DbSet<TxBySlot> TxBySlot { get; }
}

public class TxBySlotDbContext
(
    DbContextOptions options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration), ITxBySlotDbContext
{
    public DbSet<TxBySlot> TxBySlot => Set<TxBySlot>();

    override protected void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TxBySlot>(entity =>
        {
            entity.HasKey(e => new { e.Hash, e.Index, e.Slot });
        });
    }
}
