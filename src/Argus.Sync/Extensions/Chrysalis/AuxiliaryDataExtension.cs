using CardanoSharp.Wallet.Models.Transactions;
using Chrysalis.Cardano.Core;
using Chrysalis.Cardano.Core.Types.Block.Transaction;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Primitives;
using AuxiliaryData = Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet.AuxiliaryData;
using Metadata = Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet.Metadata;

namespace Argus.Sync.Extensions.Chrysalis;

public static class AuxiliaryDataExtension
{
    public static Dictionary<CborUlong, TransactionMetadatum> GetMetadata(this AuxiliaryData data)
        => data switch
        {
            PostAlonzoAuxiliaryDataMap x => x.Metadata?.Value ?? throw new InvalidOperationException("Metadata cannot be null in PostAlonzoAuxiliaryData."),
            Metadata x => x.Value,
            ShellyMaAuxiliaryData x => x.TransactionMetadata.Value,
            _ => throw new NotImplementedException()
        };

    public static object GetMetadataValue(this TransactionMetadatum data)
    => data switch
    {
        MetadatumMap x => x.Value,
        MetadatumList x => x.Value,
        MetadatumInt x => x switch
        {
            MetadatumIntLong longValue => longValue.Value,
            MetadatumIntULong ulongValue => ulongValue.Value,
            _ => throw new NotImplementedException("Unhandled MetadatumInt type.")
        },
        MetadatumBytes x => x.Value,
        MetadataText x => x.Value,
        _ => throw new NotImplementedException("Unsupported TransactionMetadatum type.")
    };


}