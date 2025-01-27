using Argus.Sync.Data.Models.Enums;
using Chrysalis.Cardano.Core.Extensions;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Converters;

namespace Argus.Sync.Data.Models;

public record OutputBySlot : IReducerModel
{
    public string Id { get; init; }
    public uint Index { get; init; }
    public ulong Slot { get; init; }
    public ulong? SpentSlot { get; set; }
    public string Address { get; init; }
    public byte[] RawCbor { get; init; }
    public DatumType DatumType { get; init; }
    public byte[] DatumData { get; init; }
    public byte[]? ReferenceScript { get; init; }
    public UtxoStatus UtxoStatus { get; set; }

    public OutputBySlot(
        string id,
        uint index,
        ulong slot,
        ulong? spentSlot,
        string address,
        byte[] rawCbor,
        DatumType datumType,
        byte[] datumData,
        byte[]? referenceScript,
        UtxoStatus utxoStatus
    )
    {
        Id = id;
        Index = index;
        Slot = slot;
        SpentSlot = spentSlot;
        Address = address;
        RawCbor = rawCbor;
        DatumType = datumType;
        DatumData = datumData;
        ReferenceScript = referenceScript;
        UtxoStatus = utxoStatus;
    }

    public (DatumType DatumType, byte[]? RawData)? Datum
    {
        get
        {
            return (DatumType, DatumData);
        }
    }

    public Value Amount
    {
        get => CborSerializer.Deserialize<TransactionOutput>(RawCbor)?.Amount()
            ?? throw new InvalidOperationException("Failed to deserialize Value from RawCbor");
    }
}