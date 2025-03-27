using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record TransactionTest(string TxHash, ulong TxIndex, ulong Slot, byte[] RawTx, DateTimeOffset CreatedAt) : IReducerModel;