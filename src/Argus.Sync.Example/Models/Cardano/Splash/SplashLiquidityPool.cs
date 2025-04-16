

using Argus.Sync.Example.Models.Cardano.Common;
using Argus.Sync.Example.Models.Cardano.Sundae;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Plutus.Address;

namespace Argus.Sync.Example.Models.Cardano.Splash;

[CborSerializable]
[CborConstr(0)]
public partial record SplashLiquidityPool(
    [CborOrder(0)]
    AssetClassConstr PoolNft,
    
    [CborOrder(1)]
    AssetClassConstr AssetX,
    
    [CborOrder(2)]
    AssetClassConstr AssetY,
    
    [CborOrder(3)]
    AssetClassConstr AssetLq,
    
    [CborOrder(4)]
    ulong Fee1,
    
    [CborOrder(5)]
    ulong Fee2,
    
    [CborOrder(6)]
    ulong Fee3,

    [CborOrder(7)]
    ulong Fee4,
    
    [CborOrder(8)]
    CborMaybeIndefList<Inline<Credential>> Verification,

    [CborOrder(9)]
    ulong MarketOpen,

    [CborOrder(10)]
    byte[] Last
) : CborBase;