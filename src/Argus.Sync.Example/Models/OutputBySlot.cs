using Argus.Sync.Data.Models;
using Argus.Sync.Data.Models.Enums;
using Argus.Sync.Reducers;

namespace Argus.Sync.Example.Models;

public record OutputBySlot : IReducerModel
{
    public string TxHash { get; init; }
    public uint TxIndex { get; init; }
    public ulong Slot { get; init; }
    public ulong? SpentSlot { get; set; }
    public string Address { get; init; }
    public byte[] RawCbor { get; init; }
    // public DatumType DatumType { get; init; }
    public byte[] DatumData { get; init; }
    public byte[]? ReferenceScript { get; init; }
    public UtxoStatus UtxoStatus { get; set; }

    public OutputBySlot(
        string txHash,
        uint txIndex,
        ulong slot,
        ulong? spentSlot,
        string address,
        byte[] rawCbor,
        // DatumType datumType,
        byte[] datumData,
        byte[]? referenceScript,
        UtxoStatus utxoStatus
    )
    {
        TxHash = txHash;
        TxIndex = txIndex;
        Slot = slot;
        SpentSlot = spentSlot;
        Address = address;
        RawCbor = rawCbor;
        // DatumType = datumType;
        DatumData = datumData;
        ReferenceScript = referenceScript;
        UtxoStatus = utxoStatus;
    }
}