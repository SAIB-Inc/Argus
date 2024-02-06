using Cardano.Sync.Data;
using Microsoft.EntityFrameworkCore;

namespace Cardano.Sync.Example.Data;

public class CardanoTestDbContext
(
    DbContextOptions<CardanoTestDbContext> options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration)
{
    override protected void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
