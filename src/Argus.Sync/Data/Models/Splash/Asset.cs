using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Argus.Sync.Data.Models.Splash;
// 121_0([_
//     h'6de26316436fa890676da9c6fddd141c440b694dbb3bba894f2370a0',
//     h'0014df105553444d5f4144415f4e4654',
// ]),
[CborSerializable(CborType.Constr, Index = 0)]
public record AssetClass(
    [CborProperty(0)]
    CborBytes PolicyId,

    [CborProperty(1)]
    CborBytes AssetName
) : ICbor;
