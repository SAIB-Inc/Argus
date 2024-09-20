
using System.Formats.Cbor;
using CborSerialization;

namespace Argus.Sync.Data.Models.Datums;

// 121_0([_ 300000_2, 121_0([_ 5, 100_0])])
[CborSerialize(typeof(RewardSettingCborConvert))]
public record RewardSetting(ulong LockDuration, Rational RewardMultiplier) : IDatum;

public class RewardSettingCborConvert : ICborConvertor<RewardSetting>
{
    public RewardSetting Read(ref CborReader reader)
    {
        var tag = reader.ReadTag();
        if ((int)tag != 121)
        {
            throw new Exception("Invalid tag");
        }
        reader.ReadStartArray();
        var lockDuration = reader.ReadUInt64();
        var rewardMultiplier = new RationalCborConvert().Read(ref reader);
        reader.ReadEndArray();
        return new RewardSetting(lockDuration, rewardMultiplier);
    }

    public void Write(ref CborWriter writer, RewardSetting value)
    {
        writer.WriteTag((CborTag)121);
        writer.WriteStartArray(null);
        writer.WriteUInt64(value.LockDuration);
        new RationalCborConvert().Write(ref writer, value.RewardMultiplier);
        writer.WriteEndArray();
    }
}