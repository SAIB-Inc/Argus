using Argus.Sync.Utils;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Block.Transaction;
using Chrysalis.Cbor;

namespace Argus.Sync.Extensions.Chrysalis;

public static class TransactionBodyExtension
{

    public static string Id(this TransactionBody txBody) =>
        Convert.ToHexString(CborSerializer.Serialize(txBody).ToBlake2b()).ToLowerInvariant();

    public static IEnumerable<TransactionInput> Inputs(this TransactionBody transactionBody)
    => transactionBody switch
    {
        ConwayTransactionBody x => x.Inputs switch
        {

            CborDefiniteList<TransactionInput> list => list.Value,
            CborIndefiniteList<TransactionInput> list => list.Value,
            CborDefiniteListWithTag<TransactionInput> list => list.Value.Value,
            CborIndefiniteListWithTag<TransactionInput> list => list.Value.Value,
            _ => throw new NotImplementedException()
        },
        BabbageTransactionBody x => x.Inputs switch
        {
            CborDefiniteList<TransactionInput> list => list.Value,
            CborIndefiniteList<TransactionInput> list => list.Value,
            CborDefiniteListWithTag<TransactionInput> tagList => tagList.Value.Value,
            CborIndefiniteListWithTag<TransactionInput> tagList => tagList.Value.Value,
            _ => throw new NotImplementedException()
        },
        AlonzoTransactionBody x => x.Inputs switch
        {
            CborDefiniteList<TransactionInput> list => list.Value,
            CborIndefiniteList<TransactionInput> list => list.Value,
            CborDefiniteListWithTag<TransactionInput> tagList => tagList.Value.Value,
            CborIndefiniteListWithTag<TransactionInput> tagList => tagList.Value.Value,
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
                CborDefiniteListWithTag<TransactionOutput> list => list.Value.Value,
                CborIndefiniteListWithTag<TransactionOutput> list => list.Value.Value,
                _ => throw new NotImplementedException()
            },
            BabbageTransactionBody x => x.Outputs switch
            {
                CborDefiniteList<TransactionOutput> list => list.Value,
                CborIndefiniteList<TransactionOutput> list => list.Value,
                CborDefiniteListWithTag<TransactionOutput> list => list.Value.Value,
                CborIndefiniteListWithTag<TransactionOutput> list => list.Value.Value,
                _ => throw new NotImplementedException()
            },
            AlonzoTransactionBody x => x.Outputs switch
            {
                CborDefiniteList<TransactionOutput> list => list.Value,
                CborIndefiniteList<TransactionOutput> list => list.Value,
                CborDefiniteListWithTag<TransactionOutput> list => list.Value.Value,
                CborIndefiniteListWithTag<TransactionOutput> list => list.Value.Value,
                _ => throw new NotImplementedException()
            },
            _ => throw new NotImplementedException()
        };
}