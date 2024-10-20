using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cardano.Models.Core.Transaction;
using TransactionOutput = Chrysalis.Cardano.Models.Core.Transaction.TransactionOutput;

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

    public static Address Address(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Address,
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Address,
            MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Address,
            ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Address,
            _ => throw new NotImplementedException()
        };

    public static Value Amount(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Amount,
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Amount,
            MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Amount,
            ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Amount,
            _ => throw new NotImplementedException()
        };

    public static MultiAssetOutput? MultiAsset(this Value value)
        => value switch
        {
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.MultiAsset,
            _ => null
        };

    public static ulong GetCoin(this Value value)
        => value switch
        {
            Lovelace lovelace => lovelace.Value,
            LovelaceWithMultiAsset lovelaceWithMultiAsset => lovelaceWithMultiAsset.Lovelace.Value,
            _ => throw new NotImplementedException()
        };

    public static string GetSubject(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .Select(v => v.Value.Value
                .Select(tokenBundle =>
                    Convert.ToHexString(v.Key.Value) + Convert.ToHexString(tokenBundle.Key.Value))
                .First())
            .First();
    }

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
}