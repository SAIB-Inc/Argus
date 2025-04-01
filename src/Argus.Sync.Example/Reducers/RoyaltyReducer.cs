// using Argus.Sync.Example.Models;
// using Chrysalis.Cbor.Extensions.Cardano.Core;
// using Chrysalis.Cbor.Extensions.Cardano.Core.Header;
// using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
// using Chrysalis.Cbor.Types.Cardano.Core;
// using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
// using Microsoft.EntityFrameworkCore;

// namespace Argus.Sync.Example.Reducers;

// public class RoyaltyReducer(
//     IDbContextFactory<TestDbContext> dbContextFactory
// ) 
// // : IReducer<Royalty>
// {
//     public async Task RollBackwardAsync(ulong slot)
//     {
//         await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

//         IQueryable<Royalty> rollbackRoyaltyEntries = dbContext.Royalties
//             .AsNoTracking()
//             .Where(b => b.Slot >= slot);

//         dbContext.Royalties.RemoveRange(rollbackRoyaltyEntries);

//         await dbContext.SaveChangesAsync();
//     }

//     public async Task RollForwardAsync(Block block)
//     {
//         await using TestDbContext dbContext = await dbContextFactory.CreateDbContextAsync();

//         IEnumerable<TransactionBody> transactions = block.TransactionBodies();

//         Dictionary<int, AuxiliaryData> auxiliaryDataSet = block.AuxiliaryDataSet();
//         (TransactionBody Tx, TransactionMetadatum Metadata, string PolicyId) matchedTransaction = transactions
//             .Select((tx, index) =>
//             {
//                 if (!auxiliaryDataSet.TryGetValue(index, out AuxiliaryData? auxData))
//                     return default;

//                 Dictionary<ulong, TransactionMetadatum>? txMetadata = auxData.Metadata()?.Value;
//                 if (txMetadata is null || !txMetadata.TryGetValue(777, out TransactionMetadatum? royaltyMetadata))
//                     return default;

//                 byte[]? mintedPolicy = tx.Mint()?
//                     .FirstOrDefault(policy => policy.Value.Value.Any(asset =>
//                         string.IsNullOrEmpty(Convert.ToHexStringLower(asset.Key))))
//                     .Key;

//                 return mintedPolicy is not null
//                     ? (Tx: tx, Metadata: royaltyMetadata, PolicyId: Convert.ToHexStringLower(mintedPolicy))
//                     : default;
//             })
//             .FirstOrDefault(result => !string.IsNullOrEmpty(result.PolicyId));

//         if (matchedTransaction.Equals(default)) return;

//         bool royaltyExists = await dbContext.Royalties.AsNoTracking()
//             .AnyAsync(r => r.PolicyId == matchedTransaction.PolicyId) ||
//             dbContext.Royalties.Local.Any(r => r.PolicyId == matchedTransaction.PolicyId);

//         if (royaltyExists) return;

//         Royalty? newRoyalty = ProcessRoyalty(matchedTransaction.Metadata, matchedTransaction.PolicyId, block);
//         if (newRoyalty is null) return;

//         dbContext.Royalties.Add(newRoyalty);
//         await dbContext.SaveChangesAsync();
//     }

//     private static Royalty? ProcessRoyalty(TransactionMetadatum royaltyMetadatum, string policyId, Block block)
//     {
//         if (royaltyMetadatum is not MetadatumMap metadataMap)
//             return null;

//         // Extract royalty share
//         decimal share = metadataMap.Value
//             .Where(kvp => kvp.Key is MetadataText { Value: "pct" or "rate" })
//             .Select(kvp => kvp.Value switch
//             {
//                 MetadatumIntLong longVal => longVal.Value,
//                 MetadatumIntUlong ulongVal => ulongVal.Value,
//                 MetadataText txt => decimal.TryParse(txt.Value, out decimal val) ? val : 0,
//                 _ => 0
//             })
//             .FirstOrDefault();

//         if (share <= 0)
//             return null;

//         List<string> addressParts = [.. metadataMap.Value
//             .Where(kvp => kvp.Key is MetadataText { Value: "addr" })
//             .SelectMany(kvp => kvp.Value switch
//             {
//                 MetadatumList list => list.Value.OfType<MetadataText>().Select(t => t.Value),
//                 MetadataText single => [single.Value],
//                 _ => []
//             })];

//         if (!addressParts.Any()) return null;

//         string address = string.Join("", addressParts);

//         if (string.IsNullOrEmpty(address)) return null;

//         return new Royalty(
//             policyId,
//             address,
//             share,
//             block.Header().HeaderBody().Slot()
//         );
//     }
// }
