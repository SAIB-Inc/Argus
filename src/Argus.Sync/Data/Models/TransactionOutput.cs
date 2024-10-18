using CborSerialization;
using Chrysalis.Cardano.Models.Core.Transaction;
using Chrysalis.Cardano.Models.Core;
using Argus.Sync.Data.Models.Enums;
using Chrysalis.Cbor;

namespace Argus.Sync.Data.Models;

public record TransactionOutput : IReducerModel
{
    public string Id { get; init; } = default!;
    public uint Index { get; init; }
    public ulong Slot { get; init; }
    public ulong? SpentSlot { get; set; }
    public string Address { get; init; } = default!;
    public byte[] AmountCbor { get; private set; } = [];
    public Datum? Datum { get; init; }
    public byte[]? ReferenceScript { get; init; }
    public UtxoStatus UtxoStatus { get; set; }

    public Value Amount
    {
        get => CborSerializer.Deserialize<Value>(AmountCbor)!;
        set => AmountCbor = CborSerializer.Serialize(value);
    }
}