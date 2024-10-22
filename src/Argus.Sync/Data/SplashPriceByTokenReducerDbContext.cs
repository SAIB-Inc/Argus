using Argus.Sync.Data.Models.Splash;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public interface ISplashPriceByTokenDbContext
{
    DbSet<PriceByToken> PriceByToken { get; }
}

public class SplashPriceByTokenDbContext
(
    DbContextOptions options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration), ISplashPriceByTokenDbContext
{
    public DbSet<PriceByToken> PriceByToken => Set<PriceByToken>();

    override protected void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PriceByToken>(entity =>
        {
            entity.HasKey(e => new { e.Slot, e.TxHash, e.TxIndex, e.PolicyId, e.AssetName });
        });
    }
}
