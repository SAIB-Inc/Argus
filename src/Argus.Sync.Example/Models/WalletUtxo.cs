using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

/// <summary>
/// A lovelace UTxO paying one of the configured watched addresses. Rows are retained
/// after being spent (<see cref="SpentSlot"/> set, not deleted) so chain rollbacks can
/// resurrect them. Live balance for an address = SUM(<see cref="Amount"/>) WHERE
/// <see cref="SpentSlot"/> IS NULL.
/// </summary>
public class WalletUtxo : IReducerModel
{
    /// <summary>Hash of the transaction that created this output.</summary>
    public string TxHash { get; set; } = default!;

    /// <summary>Index of this output within its transaction.</summary>
    public int TxIndex { get; set; }

    /// <summary>Slot in which this UTxO was created.</summary>
    public ulong Slot { get; set; }

    /// <summary>Bech32 address this UTxO pays (a watched address).</summary>
    public string Address { get; set; } = default!;

    /// <summary>Friendly name for the watched address (e.g. "A", "B").</summary>
    public string AddressName { get; set; } = default!;

    /// <summary>Lovelace value of this output.</summary>
    public ulong Amount { get; set; }

    /// <summary>Slot in which this UTxO was spent, or null if still unspent.</summary>
    public ulong? SpentSlot { get; set; }
}
