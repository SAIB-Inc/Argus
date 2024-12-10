using Argus.Sync.Data.Models.Minswap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public interface IMinswapPriceByTokenDbContext
{
    DbSet<PriceByToken> PriceByToken { get; }
}

public class MinswapPriceByTokenDbContext
(
    DbContextOptions options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration), IMinswapPriceByTokenDbContext
{
    public DbSet<PriceByToken> PriceByToken => Set<PriceByToken>();

    override protected void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PriceByToken>(entity =>
        {
            entity.HasKey(e => new { e.Slot, e.TxHash, e.TxIndex, e.TokenXSubject, e.TokenYSubject });
        });
    }
}