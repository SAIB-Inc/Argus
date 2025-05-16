using System.ComponentModel.DataAnnotations.Schema;
using Argus.Sync.Data.Models;
using Argus.Sync.Example.Models.Cardano.Sundae;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Argus.Sync.Example.Models;

public record SundaePriceByToken(
    ulong Slot,
    string TxHash,
    ulong TxIndex,
    string Identifier,
    string AssetX,
    string AssetY,
    decimal AssetXPrice,
    decimal AssetYPrice,
    string Pair,
    string LpToken,
    ulong CirculatingLp,
    ulong? SlotUpdated
) : IReducerModel;