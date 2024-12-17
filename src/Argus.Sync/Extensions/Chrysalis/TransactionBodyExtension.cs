using Argus.Sync.Utils;
using Chrysalis.Cardano.Core;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cbor;

namespace Argus.Sync.Extensions.Chrysalis;

public static class TransactionBodyExtension
{
    public static byte[] AuxiliaryDataHash(this TransactionBody transactionBody)
    => transactionBody switch
    {
        ConwayTransactionBody x when x.AuxiliaryDataHash != null => x.AuxiliaryDataHash.Value,
        BabbageTransactionBody x when x.AuxiliaryDataHash != null => x.AuxiliaryDataHash.Value,
        AlonzoTransactionBody x when x.AuxiliaryDataHash != null => x.AuxiliaryDataHash.Value,
        _ => throw new NotImplementedException("AuxiliaryDataHash is either null or not implemented for this transaction type.")
    };

    public static byte[] AddressValue(this TransactionOutput transactionOutput)
    => transactionOutput switch
    {
        BabbageTransactionOutput babbageTransactionOutput => babbageTransactionOutput.Address.Value,
        AlonzoTransactionOutput alonzoTransactionOutput => alonzoTransactionOutput.Address.Value,
        MaryTransactionOutput maryTransactionOutput => maryTransactionOutput.Address.Value,
        ShellyTransactionOutput shellyTransactionOutput => shellyTransactionOutput.Address.Value,
        _ => throw new NotImplementedException()
    };
}