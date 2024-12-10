using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record BlockTest(string BlockHash, ulong BlockNumber, ulong Slot) : IReducerModel;