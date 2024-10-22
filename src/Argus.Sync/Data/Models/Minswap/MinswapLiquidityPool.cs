using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cbor;

namespace Argus.Sync.Data.Models.Minswap;

// 121_0([_
//     121_0([_
//         122_0([_
//             h'1eae96baf29e27682ea3f815aba361a0c6059d45e4bfbe95bbd2f44a',
//         ]),
//     ]),
//     121_0([_ h'', h'']),
//     121_0([_
//         h'c48cbb3d5e57ed56e276bc45f99ab39abe94e6cd7ac39fb402da47ad',
//         h'0014df105553444d',
//     ]),
//     2110440876267_3,
//     3614573186898_3,
//     1266577571074_3,
//     75_0,
//     75_0,
//     121_0([_ 1666_1]),
//     121_0([]),
// ])



[CborSerializable(CborType.Constr, Index = 0)]
public record MinswapLiquidityPool(
    [CborProperty(0)]
    Inline<Credential> StakeCredential,
    
    [CborProperty(1)]
    AssetClass AssetX,
    
    [CborProperty(2)]
    AssetClass AssetY,
    
    [CborProperty(3)]
    CborUlong total_liquidity,
    
    [CborProperty(4)]
    CborUlong reserve_a,
    
    [CborProperty(5)]
    CborUlong reserve_b,
    
    [CborProperty(6)]
    CborUlong base_fee_a_numerator,

    [CborProperty(7)]
    CborUlong base_fee_b_numerator,
    
    [CborProperty(8)]
    Option<CborUlong> fee_sharing_numerator_opt,

    [CborProperty(9)]
    Option<CborUlong> allow_dynamic_fee

) : RawCbor;













