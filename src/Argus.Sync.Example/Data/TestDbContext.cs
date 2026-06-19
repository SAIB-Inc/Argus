using Argus.Sync.Data;
using Argus.Sync.Example.Models;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Data;

public class TestDbContext
(
    DbContextOptions<TestDbContext> options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration)
{
    public DbSet<BlockTest> BlockTests => Set<BlockTest>();
    public DbSet<TransactionTest> TransactionTests => Set<TransactionTest>();
    public DbSet<WalletUtxo> WalletUtxos => Set<WalletUtxo>();
    public DbSet<WatchedAddressBalanceSnapshot> WatchedAddressBalances => Set<WatchedAddressBalanceSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        _ = modelBuilder.Entity<BlockTest>(entity =>
        {
            _ = entity.HasKey(b => new { b.Hash, b.Slot });
        });

        _ = modelBuilder.Entity<TransactionTest>(entity =>
        {
            _ = entity.HasKey(e => new { e.TxHash, e.TxIndex });
        });

        _ = modelBuilder.Entity<WalletUtxo>(entity =>
        {
            _ = entity.HasKey(u => new { u.TxHash, u.TxIndex });
            _ = entity.HasIndex(u => u.Address);
            _ = entity.HasIndex(u => u.SpentSlot);
        });

        _ = modelBuilder.Entity<WatchedAddressBalanceSnapshot>(entity =>
        {
            _ = entity.HasKey(s => new { s.Reducer, s.AddressName, s.Slot });
        });
    }
}
