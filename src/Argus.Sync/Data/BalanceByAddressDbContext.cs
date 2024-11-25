using Argus.Sync.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Argus.Sync.Data;

public interface IBalanceByAddressDbContext
{
    DbSet<BalanceByAddress> BalanceByAddress { get; }
}

public class BalanceByAddressDbContext
(
    DbContextOptions options,
    IConfiguration configuration
) : OutputBySlotDbContext(options, configuration), IBalanceByAddressDbContext
{
    public DbSet<BalanceByAddress> BalanceByAddress => Set<BalanceByAddress>();

    override protected void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BalanceByAddress>(entity =>
        {
            entity.HasKey(e => e.Address);
        });
    }
}
