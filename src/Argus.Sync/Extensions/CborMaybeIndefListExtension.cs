using Chrysalis.Cbor.Types;

namespace Argus.Sync.Extensions;

public static class CborMaybeIndefListExtensions
{
    public static IEnumerable<T> GetValue<T>(this CborMaybeIndefList<T> self) =>
        self switch
        {
            CborDefList<T> defList => defList.Value,
            CborIndefList<T> indefList => indefList.Value,
            CborDefListWithTag<T> defListWithTag => defListWithTag.Value,
            CborIndefListWithTag<T> indefListWithTag => indefListWithTag.Value,
            _ => []
        };
}