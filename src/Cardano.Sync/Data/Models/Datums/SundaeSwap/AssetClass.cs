using System.Formats.Cbor;
using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums.SundaeSwap;

[CborSerialize(typeof(TupleCborConverter<ByteArray, ByteArray, AssetClass>))]
public record AssetClass(ByteArray PolicyId, ByteArray AssetName)
    : Tuple<ByteArray, ByteArray>(PolicyId, AssetName);