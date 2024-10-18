using Chrysalis.Cbor;

namespace Argus.Sync.Data.Models;


public record TxBySlot() : IReducerModel{
    public ulong BlockSlot { get; set; }
    public string BlockHash { get; set; }
    public byte[] Transaction { get; set; }
}