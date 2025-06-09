using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record TransactionTest(string TxHash, ulong TxIndex, ulong Slot, string BlockHash, ulong BlockHeight, byte[] RawTx, DateTimeOffset CreatedAt) : IReducerModel;