using Argus.Sync.Data;
using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Data;

public class CardanoTestDbContext
(
    DbContextOptions<CardanoTestDbContext> options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration)
{
    public DbSet<BlockBySlot> BlockBySlots => Set<BlockBySlot>();

    override protected void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
