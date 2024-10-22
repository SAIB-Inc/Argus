using Chrysalis.Cardano.Models.Core;
using Argus.Sync.Data.Models.Enums;
using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Core.Block.Transaction.Output;

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

    public Datum? Datum
    {
        get
        {
            if (DatumType == DatumType.NoDatum)
            {
                return null;
            }

            return new Datum(DatumType, DatumData);
        }
    }

    public Value Amount
    {
        get => CborSerializer.Deserialize<Value>(RawCbor)
            ?? throw new InvalidOperationException("Failed to deserialize Value from RawCbor");
    }
}