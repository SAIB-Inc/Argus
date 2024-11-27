using Chrysalis.Cardano.Core;

namespace Argus.Sync.Extensions.Chrysalis;

public static class TransactionExtension
{

    public static (Address Address, Value Amount) GetComponents(this TransactionOutput output)
        => output switch
        {
            BabbageTransactionOutput x => (x.Address, x.Amount),
            AlonzoTransactionOutput x => (x.Address, x.Amount),
            MaryTransactionOutput x => (x.Address, x.Amount),
            _ => throw new NotImplementedException($"Unsupported TransactionOutput type: {output.GetType().Name}")
        };

    public static ulong GetCoin(this Value value)
        => value switch
        {
            Lovelace lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.Lovelace.Value,
            _ => throw new NotImplementedException()
        };

    public static ulong Lovelace(this Value amount)
        => amount switch
        {
            Lovelace lovelace => lovelace.Value,
            Value value => value switch
            {
                Lovelace lovelace => lovelace.Value,
                LovelaceWithMultiAsset multiasset => multiasset.Lovelace.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };
    
    public static LovelaceWithMultiAsset TransactionValueLovelace(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset,
            _ => throw new NotImplementedException()
        };
}