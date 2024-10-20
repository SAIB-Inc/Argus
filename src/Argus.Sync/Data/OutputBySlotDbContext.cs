using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public interface IOutputBySlotDbContext
{
    DbSet<OutputBySlot> OutputBySlot { get; }
}

public class OutputBySlotDbContext
(
    DbContextOptions options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration), IOutputBySlotDbContext
{
    public DbSet<OutputBySlot> OutputBySlot => Set<OutputBySlot>();

    override protected void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OutputBySlot>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Index, e.Slot });
            entity.HasOne(e => e.Datum);
            entity.Ignore(e => e.Amount);
        });
    }
}
