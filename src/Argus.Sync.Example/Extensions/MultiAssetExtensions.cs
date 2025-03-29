using Argus.Sync.Example.Models.Cardano.Sundae;
using Argus.Sync.Example.Models.Cardano.Common;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Argus.Sync.Example.Extensions;

public static class MultiAssetExtensions
{
    public static IEnumerable<(string, string)> SubjectTuples(this MultiAssetOutput multiAssetOutput)
    {
        return multiAssetOutput.Value
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle
                    => (Convert.ToHexStringLower(v.Key), Convert.ToHexStringLower(tokenBundle.Key))));
    }

    public static Dictionary<string, ulong>? TokenBundleByPolicyId(this MultiAssetOutput multiAssetOutput, string policyId)
    {
        return multiAssetOutput.Value
            .Where(v => Convert.ToHexStringLower(v.Key) == policyId.ToLowerInvariant())
            .SelectMany(v => v.Value.Value
                .Select(tokenBundle =>
                    new KeyValuePair<string, ulong>(
                        Convert.ToHexStringLower(tokenBundle.Key),
                        tokenBundle.Value
                    )))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
}