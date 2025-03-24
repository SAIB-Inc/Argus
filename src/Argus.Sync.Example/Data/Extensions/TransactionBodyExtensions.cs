
using Chrysalis.Cardano.Core.Extensions;
using Chrysalis.Cardano.Core.Types.Block;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Input;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;

namespace Argus.Sync.Example.Data.Extensions;

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
            List<TransactionInput> orderedInputs = [.. tx.Inputs().OrderBy(e => e.TransactionId()).ThenBy(e => e.Index.Value)];

            List<(byte[]? RedeemerRaw, TransactionInput Input, TransactionBody Tx)> res = [];

            if (redeemers is RedeemerList l)
                res.AddRange(l.Value.Where(e => e.Tag.Value == 0).Select(e => (e.Data.Raw, orderedInputs[(int)e.Index.Value], tx)));

            if (redeemers is RedeemerMap m)
                res.AddRange(m.Value.ToArray().Where(e => e.Key.Tag.Value == 0).Select(e => (e.Value.Data.Raw, orderedInputs[(int)e.Key.Index.Value], tx)));

            return res;
        });
}
