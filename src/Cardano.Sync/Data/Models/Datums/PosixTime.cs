using CborSerialization;

namespace Cardano.Sync.Data.Models.Datums;

[CborSerialize(typeof(CardanoIntCborConvert<PosixTime>))]
public record PosixTime(ulong Time): CardanoInt(Time);
