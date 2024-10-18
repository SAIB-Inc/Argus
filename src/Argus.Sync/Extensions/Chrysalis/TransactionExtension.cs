using Argus.Sync.Data.Models;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cardano.Models.Core.Transaction;
using TransactionOutput = Chrysalis.Cardano.Models.Core.Transaction.TransactionOutput;
using Chrysalis.Cbor;
using Chrysalis.Utils;
using Argus.Sync.Utils;

namespace Argus.Sync.Extensions.Chrysalis;

public static class TransactionExtension
{
    public static IEnumerable<TransactionInput> Inputs(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Inputs switch
            {
                CborDefiniteList<TransactionInput> list => list.Value,
                CborIndefiniteList<TransactionInput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody x => x.Inputs switch
            {
                CborDefiniteList<TransactionInput> list => list.Value,
                CborIndefiniteList<TransactionInput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionBody x => x.Inputs switch
            {
                CborDefiniteList<TransactionInput> list => list.Value,
                CborIndefiniteList<TransactionInput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<TransactionOutput> Outputs(this TransactionBody transactionBody)
        => transactionBody switch
        {
            ConwayTransactionBody x => x.Outputs switch
            {
                CborDefiniteList<TransactionOutput> list => list.Value,
                CborIndefiniteList<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody x => x.Outputs switch
            {
                CborDefiniteList<TransactionOutput> list => list.Value,
                CborIndefiniteList<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionBody x => x.Outputs switch
            {
                CborDefiniteList<TransactionOutput> list => list.Value,
                CborIndefiniteList<TransactionOutput> list => list.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };

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

    public static byte[]? ScriptRef(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput?.ScriptRef?.Value,
            _ => null
        };

    public static DatumOption? DatumOption(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Datum,
            _ => null
        };

    public static byte[]? DatumHash(this TransactionOutput transactionOutput)
        => transactionOutput switch
        {
            AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.DatumHash.Value,
            _ => null
        };

    public static (DatumType Type, byte[] Value)? GetDatumInfo(this TransactionOutput transactionOutput)
    {
        var datumOption = transactionOutput.DatumOption();

        if (datumOption == null)
        {
            byte[]? datumHash = transactionOutput.DatumHash();
            return datumHash != null ? (DatumType.DatumHash, datumHash) : null;
        }

        return datumOption switch
        {
            DatumHashOption hashOption => (DatumType.DatumHash, hashOption.DatumHash.Value),
            InlineDatumOption inlineOption => (DatumType.InlineDatum, CborSerializer.Serialize(inlineOption.Data)),
            _ => throw new NotImplementedException($"Unsupported DatumOption type: {datumOption.GetType().Name}")
        };
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

    public static string TransactionId(this TransactionBody txBody)
    {
        return Convert.ToHexString(CborSerializer.Serialize(txBody).ToBlake2b()).ToLowerInvariant();
    }
}