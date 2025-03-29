using Argus.Sync.Data.Models;
using Argus.Sync.Example.Models.Enums;

namespace Argus.Sync.Example.Models;

public record OwnerBySlot : IReducerModel
{
    public string Address { get; init; } = default!;
    public string Subject { get; init; } = default!;
    public string PolicyId { get; init; } = default!;
    public string OutRef { get; init; } = default!;
    public ulong Quantity { get; set; }
    public ulong Slot { get; set; }
    public UtxoType TransactionType { get; set; }
    public string? SpentTxHash { get; set; }

    public OwnerBySlot(
        string address, 
        string subject, 
        string policyId, 
        string outRef, 
        ulong quantity, 
        ulong slot,
        UtxoType transactionType,
        string? spentTxHash
    )
    {
        Address = address;
        Subject = subject;
        PolicyId = policyId;
        OutRef = outRef;
        Quantity = quantity;
        Slot = slot;
        TransactionType = transactionType;
        SpentTxHash = spentTxHash;
    }
}