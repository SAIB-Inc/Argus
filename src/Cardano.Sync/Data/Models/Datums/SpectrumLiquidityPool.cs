using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

/*
newtype PoolConfig (s :: S)
    = PoolConfig
        ( Term
            s
            ( PDataRecord
                '[ "poolNft"          ':= PAssetClass
                 , "poolX"            ':= PAssetClass
                 , "poolY"            ':= PAssetClass
                 , "poolLq"           ':= PAssetClass
                 , "feeNum"           ':= PInteger
                 , "stakeAdminPolicy" ':= PBuiltinList (PAsData PCurrencySymbol)
                 , "lqBound"          ':= PInteger
                 ]
            )

121_0([_
    121_0([_
        h'1f164eea5c242f53cb2df2150fa5ab7ba126350e904ddbcc65226e18',
        h'634e4554415f4144415f504f4f4c5f4944454e54495459',
    ]),
    121_0([_ h'', h'']),
    121_0([_
        h'b34b3ea80060ace9427bda98690a73d33840e27aaa8d6edb7f0c757a',
        h'634e455441',
    ]),
    121_0([_
        h'1f164eea5c242f53cb2df2150fa5ab7ba126350e904ddbcc65226e18',
        h'4144415f634e4554415f4c50',
    ]),
    997_1,
    [_
        h'd26de68106ec5905de8d9771ae3ee6c3236caee843bfc156af35a343',
    ],
    0,
])
*/

[CborSerialize(typeof(SpectrumLiquidityPoolCborConvert))]
public record SpectrumLiquidityPool(
    AssetClass PoolNft,
    AssetClass PoolX,
    AssetClass PoolY,
    AssetClass PoolLq,
    ulong FeeNum,
    List<byte[]> StakeAdminPolicy,
    ulong LqBound
) : IDatum;

public class SpectrumLiquidityPoolCborConvert : ICborConvertor<SpectrumLiquidityPool>
{
    public SpectrumLiquidityPool Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }
        reader.ReadStartArray();
        var poolNft = new AssetClassCborConvert().Read(ref reader);
        var poolX = new AssetClassCborConvert().Read(ref reader);
        var poolY = new AssetClassCborConvert().Read(ref reader);
        var poolLq = new AssetClassCborConvert().Read(ref reader);
        var feeNum = reader.ReadUInt64();

        reader.ReadStartArray();
        var stakeAdminPolicyList = new List<byte[]>();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            stakeAdminPolicyList.Add(reader.ReadByteString());
        }
        reader.ReadEndArray();
        
        var lqBound = reader.ReadUInt64();
        
        reader.ReadEndArray();
        return new SpectrumLiquidityPool(poolNft, poolX, poolY, poolLq, feeNum, stakeAdminPolicyList, lqBound);
    }

    public void Write(ref CborWriter writer, SpectrumLiquidityPool value)
    {
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(null);
        new AssetClassCborConvert().Write(ref writer, value.PoolNft);
        new AssetClassCborConvert().Write(ref writer, value.PoolX);
        new AssetClassCborConvert().Write(ref writer, value.PoolY);
        new AssetClassCborConvert().Write(ref writer, value.PoolLq);
        writer.WriteUInt64(value.FeeNum);
        writer.WriteStartArray(null);
        foreach (var policy in value.StakeAdminPolicy)
        {
            writer.WriteByteString(policy);
        }
        writer.WriteEndArray();
        writer.WriteUInt64(value.LqBound);
        writer.WriteEndArray();
    }
}
