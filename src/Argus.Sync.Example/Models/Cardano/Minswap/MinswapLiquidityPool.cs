using Argus.Sync.Example.Models.Cardano.Common;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Plutus.Address;

namespace Argus.Sync.Example.Models.Cardano.Minswap;

[CborSerializable]
[CborConstr(0)]
public partial record MinswapLiquidityPool(
    [CborOrder(0)]
    Inline<Credential> StakeCredential,
    
    [CborOrder(1)]
    AssetClassConstr AssetX,
    
    [CborOrder(2)]
    AssetClassConstr AssetY,
    
    [CborOrder(3)]
    ulong TotalLiquidity,
    
    [CborOrder(4)]
    ulong ReserveA,
    
    [CborOrder(5)]
    ulong ReserveB,
    
    [CborOrder(6)]
    ulong BaseFeeANumerator,

    [CborOrder(7)]
    ulong BaseFeeBNumerator,
    
    [CborOrder(8)]
    Option<ulong> FeeSharingNumeratorOpt,

    [CborOrder(9)]
    Bool AllowDynamicFee
) : CborBase;











