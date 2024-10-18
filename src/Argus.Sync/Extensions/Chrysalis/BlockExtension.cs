using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cardano.Models.Core.Transaction;

namespace Argus.Sync.Extensions.Chrysalis;

public static class BlockExtension
{
    public static ulong Slot(this Block block)
        => block.Header.HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.Slot.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.Slot.Value,
            _ => throw new NotImplementedException()
        };

    public static BlockHeaderBody HeaderBody(this Block block)
        => block.Header.HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody,
            _ => throw new NotImplementedException()
        };

    public static ulong Number(this Block block)
        => block.Header.HeaderBody switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockNumber.Value,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockNumber.Value,
            _ => throw new NotImplementedException()
        };

    public static IEnumerable<TransactionBody> TransactionBodies(this Block block)
        => block.TransactionBodies switch
        {
            CborDefiniteList<TransactionBody> x => x.Value,
            CborIndefiniteList<TransactionBody> x => x.Value,
            _ => throw new NotImplementedException()
        };
}
