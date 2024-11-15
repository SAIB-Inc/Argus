using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;
using CborBytes = Chrysalis.Cardano.Cbor.CborBytes;
using Address = Chrysalis.Cardano.Plutus.Address;

namespace Argus.Sync.Data.Models.Jpg;

// 121_0([_
//     [_
//         121_0([_
//             121_0([_
//                 121_0([_
//                     h'a0adf3d71f77eef162098726fe9723a5b99b4a4e6a2a1b8001066098',
//                 ]),
//                 121_0([_
//                     121_0([_
//                         121_0([_
//                             h'2a4d6ec982ac5dffbfa35ac40bf45ad84ea34857878fd08d5784ddc7',
//                         ]),
//                     ]),
//                 ]),
//             ]),
//             15000000_2,
//         ]),
//         121_0([_
//             121_0([_
//                 121_0([_
//                     h'2cafdf13b6df999ce9aa5101bd62a44f82033d8797f46772744ba746',
//                 ]),
//                 121_0([_
//                     121_0([_
//                         121_0([_
//                             h'860091a39b4387a7a470f02d8f4623e3379545824808ca0050d4c76c',
//                         ]),
//                     ]),
//                 ]),
//             ]),
//             279000000_2,
//         ]),
//     ],
//     h'2cafdf13b6df999ce9aa5101bd62a44f82033d8797f46772744ba746'
// ])

[CborSerializable(CborType.Constr, Index = 0)]
public record Listing(
    [CborProperty(0)]
    CborIndefiniteList<ListingPayout> Payouts,

    [CborProperty(1)]
    CborBytes OwnerPkh
) : RawCbor;

//[_
//         121_0([_
//             121_0([_
//                 121_0([_
//                     h'a0adf3d71f77eef162098726fe9723a5b99b4a4e6a2a1b8001066098',
//                 ]),
//                 121_0([_
//                     121_0([_
//                         121_0([_
//                             h'2a4d6ec982ac5dffbfa35ac40bf45ad84ea34857878fd08d5784ddc7',
//                         ]),
//                     ]),
//                 ]),
//             ]),
//             15000000_2,
//         ]),
//         121_0([_
//             121_0([_
//                 121_0([_
//                     h'2cafdf13b6df999ce9aa5101bd62a44f82033d8797f46772744ba746',
//                 ]),
//                 121_0([_
//                     121_0([_
//                         121_0([_
//                             h'860091a39b4387a7a470f02d8f4623e3379545824808ca0050d4c76c',
//                         ]),
//                     ]),
//                 ]),
//             ]),
//             279000000_2,
//         ]),
//     ]
[CborSerializable(CborType.Constr, Index = 0)]
public record ListingPayout(
    [CborProperty(0)]
    Address Address,

    [CborProperty(1)]
    CborUlong Amount
) : RawCbor;


//            121_0([_
//                 121_0([_
//                     h'a0adf3d71f77eef162098726fe9723a5b99b4a4e6a2a1b8001066098',
//                 ]),
//                 121_0([_
//                     121_0([_
//                         121_0([_
//                             h'2a4d6ec982ac5dffbfa35ac40bf45ad84ea34857878fd08d5784ddc7',
//                         ]),
//                     ]),
//                 ]),
//             ])



