using Argus.Sync.Data.Models.Jpg;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public interface IJpgPriceByTokenDbContext
{
    DbSet<PriceByToken> PriceByToken { get; }
}

public class JpgPriceByTokenDbContext
(
    DbContextOptions options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration), IJpgPriceByTokenDbContext
{
    public DbSet<PriceByToken> PriceByToken => Set<PriceByToken>();

    override protected void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PriceByToken>(entity =>
        {
            entity.HasKey(e => new { e.Slot, e.TxHash, e.TxIndex, e.Subject });
        });
    }
}