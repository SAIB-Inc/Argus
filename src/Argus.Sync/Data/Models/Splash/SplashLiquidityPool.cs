using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cbor;

namespace Argus.Sync.Data.Models.Splash;

// 121_0([
//     121_0([
//         h'6de26316436fa890676da9c6fddd141c440b694dbb3bba894f2370a0',
//         h'0014df105553444d5f4144415f4e4654',
//     ]),
//     121_0([h'', h'']),
//     121_0([
//         h'c48cbb3d5e57ed56e276bc45f99ab39abe94e6cd7ac39fb402da47ad',
//         h'0014df105553444d',
//     ]),
//     121_0([
//         h'392e87503b106a3d32b3f0bb2826f4e8ff849dccb2b3ac948db4c10e',
//         h'0014df105553444d5f4144415f4c51',
//     ]),
//     99900_2,
//     10,
//     1496705_2,
//     503539_2,
//     [
//         121_0([
//             122_0([
//                 h'c1a77381e01124346410759ccd32dedae9c702ff8cb24246815f1ab0',
//             ]),
//         ]),
//     ],
//     0,
//     h'75c4570eb625ae881b32a34c52b159f6f3f3f2c7aaabf5bac4688133',
// ])



[CborSerializable(CborType.Constr, Index = 0)]
public record SplashLiquidityPool(
    [CborProperty(0)]
    AssetClass PoolNft,
    
    [CborProperty(1)]
    AssetClass AssetX,
    
    [CborProperty(2)]
    AssetClass AssetY,
    
    [CborProperty(3)]
    AssetClass AssetLq,
    
    [CborProperty(4)]
    CborUlong Fee1,
    
    [CborProperty(5)]
    CborUlong Fee2,
    
    [CborProperty(6)]
    CborUlong Fee3,

    [CborProperty(7)]
    CborUlong Fee4,
    
    [CborProperty(8)]
    CborIndefiniteList<Inline<Credential>> Verification,

    [CborProperty(9)]
    CborUlong MarketOpen,

    [CborProperty(10)]
    CborBytes Last

) : RawCbor;

