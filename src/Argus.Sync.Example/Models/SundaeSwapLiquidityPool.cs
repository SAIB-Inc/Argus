using System.ComponentModel.DataAnnotations.Schema;
using Argus.Sync.Data.Models;
using Argus.Sync.Example.Models.Datums;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Argus.Sync.Example.Models;

public record SundaeSwapLiquidityPool(
    ulong Slot,
    string Outref,
    string Identifier,
    string AssetX,
    string AssetY,
    string Pair,
    string LpToken,
    ulong CirculatingLp,
    byte[] TxOutputRaw
) : IReducerModel
{
    [NotMapped]
    public TransactionOutput TxOutput => TransactionOutput.Read(TxOutputRaw);

    [NotMapped]
    public SundaeSwapLiquidityPoolDatum Datum => SundaeSwapLiquidityPoolDatum.Read(TxOutput.DatumOption()!.Data());
}