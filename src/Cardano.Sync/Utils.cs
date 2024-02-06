using PallasDotnet.Models;
using TransactionOutputEntity = Cardano.Sync.Data.Models.TransactionOutput;
using ValueEntity = Cardano.Sync.Data.Models.Value;
using DatumEntity = Cardano.Sync.Data.Models.Datum;

namespace Cardano.Sync;

public static class Utils
{
    public static TransactionOutputEntity MapTransactionOutputEntity(string TransactionId, ulong slot, TransactionOutput output)
    {
        return new TransactionOutputEntity
        {
            Id = TransactionId,
            Address = output.Address.ToBech32(),
            Slot = slot,
            Index = Convert.ToUInt32(output.Index),
            Datum = output.Datum is null ? null : new DatumEntity((Data.Models.DatumType)output.Datum.Type, output.Datum.Data),
            Amount = new ValueEntity
            {
                Coin = output.Amount.Coin,
                MultiAsset = output.Amount.MultiAsset.ToDictionary(
                    k => k.Key.ToHex(),
                    v => v.Value.ToDictionary(
                        k => k.Key.ToHex(),
                        v => v.Value
                    )
                )
            }
        };
    }
}