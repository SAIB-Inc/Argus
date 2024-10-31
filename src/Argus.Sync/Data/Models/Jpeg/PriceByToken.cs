using Argus.Sync.Data.Models.Enums;

namespace Argus.Sync.Data.Models.Jpeg;

public record PriceByToken : IReducerModel
{
    public ulong Slot { get; init; }
    public ulong? SpentSlot { get; set; }
    public string TxHash { get; init; }
    public ulong TxIndex { get; init; }
    public ulong Price { get; set; } = default;
    public string Subject { get; init; }
    public UtxoStatus? Status { get; set; }

    public PriceByToken(
        ulong Slot,
        ulong? SpentSlot,
        string TxHash,
        ulong TxIndex,
        ulong Price,
        string Subject,
        UtxoStatus? Status
    )
    {
        this.Slot = Slot;
        this.SpentSlot = SpentSlot;
        this.TxHash = TxHash;
        this.TxIndex = TxIndex;
        this.Price = Price;
        this.Subject = Subject;
        this.Status = Status;
    }

    public string PolicyId => Subject[..56];

    public string AssetName => Subject[56..];
}