using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

/// <summary>
/// Per-slot snapshot of a watched address's unspent balance, written by a reducer that DEPENDS ON
/// <c>LovelaceBalanceByAddressReducer</c> and derives the value from that parent's
/// <see cref="WalletUtxo"/> rows. The <see cref="Reducer"/> discriminator lets multiple
/// fork-sibling dependents share one table. The latest row per (Reducer, AddressName) is the
/// current balance; rows are slot-keyed so rollbacks can delete those at/after a slot.
/// </summary>
public class WatchedAddressBalanceSnapshot : IReducerModel
{
    /// <summary>Name of the dependent reducer that wrote this row (fork-sibling discriminator).</summary>
    public string Reducer { get; set; } = default!;

    /// <summary>Friendly name of the watched address (e.g. "A", "B").</summary>
    public string AddressName { get; set; } = default!;

    /// <summary>Bech32 address this snapshot is for.</summary>
    public string Address { get; set; } = default!;

    /// <summary>Slot this snapshot was taken at.</summary>
    public ulong Slot { get; set; }

    /// <summary>Unspent lovelace balance of the address as of <see cref="Slot"/>.</summary>
    public ulong Balance { get; set; }
}
