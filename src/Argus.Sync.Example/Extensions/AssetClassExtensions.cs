// using Argus.Sync.Example.Models.Cardano.Sundae;

// namespace Argus.Sync.Example.Extensions;

// public static class AssetClassExtensions
// {
//     public static IEnumerable<(string PolicyId, string AssetName)> SubjectTuples(this AssetClass assetClass)
//     {
//         return assetClass switch
//         {
//             AssetClassList assetClassList => [(Convert.ToHexStringLower(assetClassList.PolicyId), Convert.ToHexStringLower(assetClassList.AssetName))],
//             AssetClassConstr assetClassConstr => [(Convert.ToHexStringLower(assetClassConstr.PolicyId), Convert.ToHexStringLower(assetClassConstr.AssetName))],
//             _ => []
//         };
//     }
// }