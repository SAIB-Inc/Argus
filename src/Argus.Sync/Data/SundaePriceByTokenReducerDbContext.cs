using Argus.Sync.Data.Models.SundaeSwap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public interface ISundaePriceByTokenDbContext
{
    DbSet<PriceByToken> PriceByToken { get; }
}

public class SundaePriceByTokenDbContext
(
    DbContextOptions options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration), ISundaePriceByTokenDbContext
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