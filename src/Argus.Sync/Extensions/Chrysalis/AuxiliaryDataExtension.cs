using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Core;

namespace Argus.Sync.Extensions.Chrysalis;

public static class AuxiliaryDataExtension
{
    public static Dictionary<CborUlong, TransactionMetadatum> GetMetadata (this AuxiliaryData data)
        => data switch
        {
            PostAlonzoAuxiliaryData x => x.Value.Metadata?.Value ?? throw new InvalidOperationException("Metadata cannot be null in PostAlonzoAuxiliaryData."), 
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