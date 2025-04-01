using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public class TxBySlot : IReducerModel
{
    public string TxHash { get; set; } = null!;
    public ulong Index { get; set; }
    public ulong Slot { get; set; }
    public byte[] Raw { get; set; } = null!;
    public ulong Fee { get; set; }

    public IEnumerable<byte[]> InputsRaw { get; set; } = [];
    public IEnumerable<byte[]> OutputsRaw { get; set; } = [];

    public TxBySlot() { }

    public TxBySlot(
        string txHash,
        ulong index,
        ulong slot,
        byte[] raw,
        IEnumerable<byte[]> inputsRaw,
        IEnumerable<byte[]> outputsRaw,
        ulong fee
    )
    {
        TxHash = txHash;
        Index = index;
        Slot = slot;
        Raw = raw;
        Fee = fee;
        InputsRaw = inputsRaw;
        OutputsRaw = outputsRaw;
    }
}
