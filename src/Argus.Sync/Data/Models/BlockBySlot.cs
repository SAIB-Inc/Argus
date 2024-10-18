using Chrysalis.Cbor;

namespace Argus.Sync.Data.Models;

public record BlockBySlot() : IReducerModel
{
    public ulong Slot { get; set; }
    public string Hash { get; set; }
    public byte[] Block { get; set; } 
}