using Chrysalis.Cardano.Models.Core.Block;

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
}