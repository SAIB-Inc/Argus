namespace Argus.Sync.Data.Models;

public record BalanceByAddress() : IReducerModel
{
    public string Address { get; set; } = default!;
    public ulong Balance { get; set; } = default!;
}