using Argus.Sync.Example.Data.Cardano;
using Argus.Sync.Example.Models;
using Chrysalis.Cardano.Core.Extensions;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Converters;

namespace Argus.Sync.Example.Data.Extensions;

public static class OrderBySlotExtensions
{
    public static bool IsAcceptOrCancelRedeemer(
        this OrderBySlot self, 
        IEnumerable<(byte[]? RedeemerRaw, 
        TransactionInput Input, 
        TransactionBody Tx)> inputRedeemers
    )
    {
        // Get the input that spent this listing
        byte[]? redeemerRaw = inputRedeemers
            .Where(ir => ir.Input.TransactionId() == self.TxHash && ir.Input.Index() == self.Index)
            .Select(ir => ir.RedeemerRaw)
            .FirstOrDefault();

        if (redeemerRaw is null) return false;

        try
        {
            AcceptRedeemer? crashrBuyRedeemer = CborSerializer.Deserialize<AcceptRedeemer>(redeemerRaw);
            return true;
        }
        catch { }

        return false;
    }
}
