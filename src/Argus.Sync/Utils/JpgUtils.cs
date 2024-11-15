using System.Text;
using Argus.Sync.Extensions.Chrysalis;
using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;

namespace Argus.Sync.Utils;

public static class JpgUtils
{
    public static Dictionary<string, byte[]> MapMetadataToDatumDictionary(AuxiliaryData data)
    {
        Dictionary<string, byte[]> datumDict = [];

        StringBuilder datumBuild = new();

        foreach (KeyValuePair<CborUlong, TransactionMetadatum> metaDict in data.GetMetadata())
        {
            if (metaDict.Key.Value == 30) continue;

            object value = metaDict.Value.GetMetadataValue();
            if (value is string strValue)
            {
                datumBuild.Append(strValue);
            }
        }

        string datumHex = datumBuild.ToString().TrimEnd(',');
        string[] datumArr = datumHex.Split(',');

        datumDict = datumArr
            .Select(e => Convert.FromHexString(e)).ToArray()
            .DistinctBy(datum => Convert.ToHexString(datum.ToBlake2b()))
            .ToDictionary(datum => Convert.ToHexString(datum.ToBlake2b()), datum => datum);

        return datumDict;
    }

}

