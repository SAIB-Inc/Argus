
namespace Argus.Sync.Data.Models;

public record TestModel : IReducerModel
{
    public ulong Slot { get; set; }
    public ulong BlockNumber { get; set; }
}