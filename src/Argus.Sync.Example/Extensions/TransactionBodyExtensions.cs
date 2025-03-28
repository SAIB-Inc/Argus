using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Argus.Sync.Example.Extensions;

public static class TransactionBodyExtensions
{
    public static IEnumerable<(byte[]? RedeemerRaw, TransactionInput Input, TransactionBody Tx)> GetInputRedeemerTuple(
        this List<TransactionBody> self, 
        Block block
    ) =>
        self.SelectMany(tx =>
        {
            IEnumerable<TransactionOutput> outputs = tx.Outputs();
            int index = self.IndexOf(tx);
            TransactionWitnessSet witnessSet = block.TransactionWitnessSets().ElementAt(index);
            Redeemers? redeemers = witnessSet.Redeemers();
            List<TransactionInput> orderedInputs = [.. tx.Inputs().OrderBy(e => e.TransactionId()).ThenBy(e => e.Index)];

            List<(byte[]? RedeemerRaw, TransactionInput Input, TransactionBody Tx)> res = [];

            if (redeemers is RedeemerList l)
                res.AddRange(l.Value.Where(e => e.Tag == 0).Select(e => (e.Data.Raw!.Value.ToArray() ?? [], orderedInputs[(int)e.Index], tx))!);

            if (redeemers is RedeemerMap m)
                res.AddRange(m.Value.ToArray().Where(e => e.Key.Tag == 0).Select(e => (e.Value.Data.Raw!.Value.ToArray() ?? [], orderedInputs[(int)e.Key.Index], tx))!);

            return res;
        });
}