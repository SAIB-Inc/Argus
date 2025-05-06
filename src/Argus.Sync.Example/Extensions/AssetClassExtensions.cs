
using Argus.Sync.Example.Models.Cardano.Sundae;

namespace Argus.Sync.Example.Extensions;

public static class AssetClassExtensions
{
    public static string PolicyId(this AssetClass asset)
        => asset switch
        {
            { PolicyId: { Length: > 0 } } => Convert.ToHexStringLower(asset.PolicyId),
            _ => string.Empty
        };

    public static string AssetName(this AssetClass asset)
        => asset switch
        {
            { AssetName: { Length: > 0 } } => Convert.ToHexStringLower(asset.AssetName),
            _ => string.Empty
        };
}