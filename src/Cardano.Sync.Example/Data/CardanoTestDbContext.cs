using Cardano.Sync.Data;
using Cardano.Sync.Data.Models;
using Cardano.Sync.Reducers;
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

        modelBuilder.Entity<Block>();
    }
}
