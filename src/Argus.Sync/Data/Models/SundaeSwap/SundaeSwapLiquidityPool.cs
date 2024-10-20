using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cardano.Models.Sundae;
using Chrysalis.Cbor;

namespace Argus.Sync.Data.Models.SundaeSwap;
// 121_0([_
//         h'c7ef237f227542a0c8930d37911491c56a341fdef8437e0f21d024f8',
//         [_
//                 [_ h'', h''],
//             [_
//                 h'f66d78b4a3cb3d37afa0ec36461e51ecbde00f26c8f0a68f94b69880',
//                 h'69555344',
//             ],
//         ],
//         351261317082_3,
//         5,
//         5,
//         121_0([_
//             124_0([_
//             3,
//             [_
//                 121_0([_
//                 h'8582e6a55ccbd7af4cabe35d6da6eaa3d543083e1ce822add9917730',
//                 ]),
//                 121_0([_
//                     h'7180d7ad9aaf20658d8f88c32a2e5c287425618c32c9bb82d6b6c8f8',
//                 ]),
//                 121_0([_
//                     h'bba4dff30f517f2859f8f295a97d3d85f26a818078f9294256fda2d8',
//                 ]),
//                 121_0([_
//                     h'1f68495896a7ba5132198145359311e991a1463e95ccc6f56703653d',
//                 ]),
//                 121_0([_
//                     h'f65e667d512b26aa98a97ac22e958e5201e7ea279d74b2e4ec5883db',
//                 ]),
//             ],
//             ]),
//         ]),
//         0,
//         279036000_2,
//     ])
[CborSerializable(CborType.Constr, Index = 0)]
public record SundaeSwapLiquidityPool(
    [CborProperty(0)]
    CborBytes Identifier,
    
    [CborProperty(1)]
    AssetClassTuple Assets,
    
    [CborProperty(2)]
    CborUlong CirculatingLp,
    
    [CborProperty(3)]
    CborUlong BidFeesPer10Thousand,
    
    [CborProperty(4)]
    CborUlong AskFeesPer10Thousand,
    
    [CborProperty(5)]
    Option<MultisigScript> FeeManager,
    
    [CborProperty(6)]
    CborUlong MarketOpen,
    
    [CborProperty(7)]
    CborUlong ProtocolFees
) : RawCbor;

