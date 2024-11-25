namespace Argus.Sync.Data.Models;

public record BalanceByAddress : IReducerModel
{
    public string Address { get; init; }
    public ulong Balance { get; set; }

    public BalanceByAddress(string address, ulong balance)
    {
        Address = address;
        Balance = balance;
    }
}