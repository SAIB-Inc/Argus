using Chrysalis.Cardano.Models.Core.Transaction;

namespace Argus.Sync.Extensions.Chrysalis;

public static class TransactionInputExtension
{
    public static string TransacationId(this TransactionInput transactionInput) 
        => Convert.ToHexString(transactionInput.TransactionId.Value).ToLowerInvariant();

    public static int Index(this TransactionInput transactionInput) 
        => transactionInput.Index.Value;
}