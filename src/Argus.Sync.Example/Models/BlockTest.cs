using Argus.Sync.Data.Models;

namespace Argus.Sync.Example.Models;

public record BlockTest(string Hash, ulong Height, ulong Slot, DateTime CreatedAt) : IReducerModel;