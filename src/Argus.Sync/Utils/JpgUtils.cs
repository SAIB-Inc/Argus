using System.Text;
using Chrysalis.Cardano.Core.Extensions;
using Chrysalis.Cardano.Core.Types.Block.Transaction;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;


namespace Argus.Sync.Utils;

public static class JpgUtils
{
    public static Dictionary<string, byte[]> MapMetadataToDatumDictionary(AuxiliaryData data)
    {
        Dictionary<string, byte[]> datumDict = [];

        StringBuilder datumBuild = new();

        foreach (KeyValuePair<ulong, TransactionMetadatum> metaDict in data.GetMetadata())
        {
            if (metaDict.Key == 30) continue;

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
