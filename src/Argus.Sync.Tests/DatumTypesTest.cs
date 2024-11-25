// using Argus.Sync.Data.Models.Datums;
// using Argus.Sync.Data.Models;
// using Argus.Sync.Data.Models.Experimental;
// using System.Formats.Cbor;
// using AssetClass = Argus.Sync.Data.Models.Datums.SundaeSwap.AssetClass;
// using Crashr.Data.Models.Datums;

// namespace Argus.Sync.Tests;

// public class DatumTypesTest
// {
//     [Fact]
//     public void DictionaryDatumTest()
//     {
//         var dictionary = new Dictionary<CardanoInt>
//         {
//             {
//                 new ByteArray([1, 2, 3]),
//                 new CardanoInt(42)
//             }
//         };

//         bool hasKey = dictionary.ContainsKey(new ByteArray([1, 2, 3]));
//         bool hasValue = dictionary.Contains(new KeyValuePair<ByteArray, CardanoInt>(
//             new ByteArray([1, 2, 3]), 
//             new CardanoInt(42)
//         ));

//         dictionary[new ByteArray([1, 2, 4])] = new CardanoInt(43);
//         dictionary.Remove(new ByteArray([1, 2, 3]));

//         bool isRemoved = !dictionary.ContainsKey(new ByteArray([1, 2, 3]));

//         Assert.True(hasKey && hasValue && isRemoved);
//     }
// }