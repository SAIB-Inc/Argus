using Argus.Sync.Reducers;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Argus.Sync.Tests.Mongo;

/// <summary>A lovelace UTxO document for a watched address (the Mongo analogue of the EF WalletUtxo).</summary>
public sealed class MongoWalletUtxo
{
    /// <summary>Composite id "TxHash:TxIndex".</summary>
    [BsonId]
    public string Id { get; set; } = default!;

    /// <summary>Creating transaction hash.</summary>
    public string TxHash { get; set; } = default!;

    /// <summary>Output index within the transaction.</summary>
    public int TxIndex { get; set; }

    /// <summary>Slot the UTxO was created in.</summary>
    [BsonRepresentation(BsonType.Int64)]
    public ulong Slot { get; set; }

    /// <summary>Bech32 watched address.</summary>
    public string Address { get; set; } = default!;

    /// <summary>Friendly name ("A"/"B").</summary>
    public string AddressName { get; set; } = default!;

    /// <summary>Lovelace value.</summary>
    [BsonRepresentation(BsonType.Int64)]
    public ulong Amount { get; set; }

    /// <summary>Slot it was spent in, or null if unspent.</summary>
    [BsonRepresentation(BsonType.Int64)]
    public ulong? SpentSlot { get; set; }
}

/// <summary>
/// MongoDB port of the example <c>LovelaceBalanceByAddressReducer</c>: tracks watched-address lovelace
/// UTxOs as Mongo documents written through the unit-of-work's session, so they commit atomically with
/// the reducer's checkpoint. Proves a non-EF backend runs the same indexing + rollback logic.
/// </summary>
public sealed class MongoLovelaceBalanceReducer(IConfiguration configuration) : IReducer
{
    private readonly Dictionary<string, (string Name, string Bech32)> _watched = ReadWatched(configuration);

    private static Dictionary<string, (string Name, string Bech32)> ReadWatched(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection("Example:WatchedAddresses").GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c["Hex"]))
            .ToDictionary(c => c["Hex"]!.ToUpperInvariant(), c => (c["Name"] ?? "?", c["Bech32"] ?? c["Hex"]!));
    }

    /// <inheritdoc />
    public async Task RollForwardAsync(IBlock block, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(uow);
        MongoStorage storage = uow.GetStorage<MongoStorage>();
        IMongoCollection<MongoWalletUtxo> utxos = storage.Collection<MongoWalletUtxo>("WalletUtxos");
        ulong slot = block.Header().HeaderBody().Slot();

        HashSet<(string TxHash, int TxIndex)> spentRefs = [];
        List<MongoWalletUtxo> created = [];

        foreach (ITransactionBody tx in block.TransactionBodies())
        {
            foreach ((string spentTxHash, ulong spentIndex) in tx.Inputs()
                .Select(i => (Convert.ToHexString(i.TransactionId.Span), i.Index)))
            {
                _ = spentRefs.Add((spentTxHash, (int)spentIndex));
            }

            string txHash = tx.Hash();
            int index = 0;
            foreach (ITransactionOutput output in tx.Outputs())
            {
                string addressHex = Convert.ToHexString(output.Address().Span);
                if (_watched.TryGetValue(addressHex, out (string Name, string Bech32) info))
                {
                    created.Add(new MongoWalletUtxo
                    {
                        Id = $"{txHash}:{index}",
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

        bool changed = false;

        // Record new watched UTxOs first so a same-block spend can see them (read-your-own-writes
        // within the session's transaction — the Mongo analogue of EF's .Local).
        if (created.Count > 0)
        {
            await utxos.InsertManyAsync(storage.Session, created, cancellationToken: ct).ConfigureAwait(false);
            changed = true;
        }

        if (spentRefs.Count > 0)
        {
            HashSet<string> spentTxHashes = [.. spentRefs.Select(r => r.TxHash)];
            List<MongoWalletUtxo> candidates = await utxos
                .Find(storage.Session, x => x.SpentSlot == null && spentTxHashes.Contains(x.TxHash))
                .ToListAsync(ct).ConfigureAwait(false);

            foreach (MongoWalletUtxo utxo in candidates)
            {
                if (spentRefs.Contains((utxo.TxHash, utxo.TxIndex)))
                {
                    _ = await utxos.UpdateOneAsync(
                        storage.Session,
                        Builders<MongoWalletUtxo>.Filter.Eq(x => x.Id, utxo.Id),
                        Builders<MongoWalletUtxo>.Update.Set(x => x.SpentSlot, slot),
                        cancellationToken: ct).ConfigureAwait(false);
                    changed = true;
                }
            }
        }

        // Mongo has no change-tracker, so a write must be announced explicitly.
        if (changed)
        {
            uow.MarkDataChanged();
        }
    }

    /// <inheritdoc />
    public async Task RollBackwardAsync(ulong slot, IBlockUnitOfWork uow, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uow);
        MongoStorage storage = uow.GetStorage<MongoStorage>();
        IMongoCollection<MongoWalletUtxo> utxos = storage.Collection<MongoWalletUtxo>("WalletUtxos");

        // Undo outputs created at/after the rollback slot.
        _ = await utxos.DeleteManyAsync(storage.Session, x => x.Slot >= slot, cancellationToken: ct).ConfigureAwait(false);

        // Resurrect UTxOs spent at/after the rollback slot.
        _ = await utxos.UpdateManyAsync(
            storage.Session,
            x => x.Slot < slot && x.SpentSlot >= slot,
            Builders<MongoWalletUtxo>.Update.Set(x => x.SpentSlot, null),
            cancellationToken: ct).ConfigureAwait(false);

        uow.MarkDataChanged();
    }
}
