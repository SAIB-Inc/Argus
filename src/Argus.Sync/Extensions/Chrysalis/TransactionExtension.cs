using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cardano.Models.Core.Transaction;

namespace Argus.Sync.Extensions.Chrysalis;

public static class TransactionExtension
{
    public static IEnumerable<TransactionBody> TransactionBodies(this Block block)
        => block.TransactionBodies switch
        {
            CborDefiniteList<TransactionBody> x => x.Value,
            CborIndefiniteList<TransactionBody> x => x.Value,
            _ => throw new NotImplementedException()
        };

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
}
