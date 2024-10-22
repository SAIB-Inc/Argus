using Chrysalis.Cardano.Models.Core.Block.Transaction.Output;

namespace Argus.Sync.Extensions.Chrysalis;

public static class ValueExtension
{
    public static LovelaceWithMultiAsset TransactionValueLovelace(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset,
            _ => throw new NotImplementedException()
        };
}