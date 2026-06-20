using Argus.Sync.Example.Data;
using Argus.Sync.Example.Models;
using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;

namespace Argus.Sync.Example.Reducers;

/// <summary>
/// Tracks the lovelace UTxO set (and therefore the balance) of a configured set of
/// watched addresses. Outputs paying a watched address are recorded as live UTxOs;
/// inputs that spend a recorded UTxO mark it spent (retained for rollback safety).
/// Watched addresses are matched on raw address bytes (base16) from
/// <c>Example:WatchedAddresses</c>, so no bech32 decoding is needed in the hot path.
/// </summary>
public class LovelaceBalanceByAddressReducer : IReducer
{
    // raw address hex (base16) -> (friendly name, bech32) for the watched set.
    private readonly Dictionary<string, (string Name, string Bech32)> _watched;

    public LovelaceBalanceByAddressReducer(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _watched = configuration
            .GetSection("Example:WatchedAddresses")
            .GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c["Hex"]))
            .ToDictionary(
                c => c["Hex"]!.ToLowerInvariant(),
                c => (c["Name"] ?? "?", c["Bech32"] ?? c["Hex"]!));
    }

    public async Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(uow);
        TestDbContext ctx = uow.GetStorage<TestDbContext>();
        ulong slot = block.Header().HeaderBody().Slot();

        HashSet<(string TxHash, int TxIndex)> spentRefs = [];
        List<WalletUtxo> created = [];

        foreach (ITransactionBody tx in block.TransactionBodies())
        {
            // tx.Hash() (used below for created rows) returns UPPERCASE hex, so hex the
            // spent input id uppercase too, otherwise spend-matching never hits.
            foreach ((string spentTxHash, ulong spentIndex) in tx.Inputs()
                .Select(i => (Convert.ToHexString(i.TransactionId.Span), i.Index)))
            {
                _ = spentRefs.Add((spentTxHash, (int)spentIndex));
            }

            string txHash = tx.Hash();
            int index = 0;
            foreach (ITransactionOutput output in tx.Outputs())
            {
                string addressHex = Convert.ToHexStringLower(output.Address().Span);
                if (_watched.TryGetValue(addressHex, out (string Name, string Bech32) info))
                {
                    created.Add(new WalletUtxo
                    {
                        TxHash = txHash,
                        TxIndex = index,
                        Slot = slot,
                        Address = info.Bech32,
                        AddressName = info.Name,
                        Amount = output.Amount().Lovelace(),
                        SpentSlot = null,
                    });
                }

                index++;
            }
        }

        // Record new watched UTxOs first so a same-block spend can see them via Local.
        if (created.Count > 0)
        {
            ctx.WalletUtxos.AddRange(created);
        }

        // Mark any of our recorded UTxOs that these inputs spend (local-first, then DB).
        if (spentRefs.Count > 0)
        {
            HashSet<string> spentTxHashes = [.. spentRefs.Select(r => r.TxHash)];
            List<WalletUtxo> candidates =
            [
                .. ctx.WalletUtxos.Local.Where(u => u.SpentSlot == null && spentTxHashes.Contains(u.TxHash)),
                .. await ctx.WalletUtxos
                    .Where(u => u.SpentSlot == null && spentTxHashes.Contains(u.TxHash))
                    .ToListAsync(ct),
            ];

            foreach (WalletUtxo utxo in candidates.DistinctBy(u => (u.TxHash, u.TxIndex)))
            {
                if (spentRefs.Contains((utxo.TxHash, utxo.TxIndex)))
                {
                    utxo.SpentSlot = slot;
                }
            }
        }
    }

    public async Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        TestDbContext ctx = uow.GetStorage<TestDbContext>();

        // Undo outputs created at or after the rollback slot.
        List<WalletUtxo> createdAtOrAfter = await ctx.WalletUtxos
            .Where(u => u.Slot >= slot)
            .ToListAsync(ct);
        ctx.WalletUtxos.RemoveRange(createdAtOrAfter);

        // Resurrect UTxOs that were spent at or after the rollback slot.
        List<WalletUtxo> spentAtOrAfter = await ctx.WalletUtxos
            .Where(u => u.Slot < slot && u.SpentSlot >= slot)
            .ToListAsync(ct);
        foreach (WalletUtxo utxo in spentAtOrAfter)
        {
            utxo.SpentSlot = null;
        }
    }
}
