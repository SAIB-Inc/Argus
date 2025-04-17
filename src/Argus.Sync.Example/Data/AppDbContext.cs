using Argus.Sync.Data;
using Argus.Sync.Example.Models;
using Microsoft.EntityFrameworkCore;

public class AppDbContext
(
    DbContextOptions<AppDbContext> options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration)
{
    public DbSet<SundaeSwapLiquidityPool> SundaeSwapLiquidityPools => Set<SundaeSwapLiquidityPool>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SundaeSwapLiquidityPool>(entity =>
        {
            entity.HasKey(e => new { e.Identifier, e.Outref });
        });
    }
}