using System.ComponentModel.DataAnnotations.Schema;
using Argus.Sync.Data.Models.Enums;

namespace Argus.Sync.Data.Models;

public record JpegPriceBySubject : IReducerModel
{
    public ulong Slot { get; init; }
    public string TxHash { get; init; } = default!;
    public ulong TxIndex { get; init; }
    public ulong Price { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public UtxoStatus? Status { get; set; }

    [NotMapped]
    public string PolicyId 
    {
        get
        {
            try
            {
                return Subject[..56];
            }
            catch
            {
                throw new Exception("Cannot derived PolicyId from Subject");
            }
        }

        set
        {
            try
            {
                Subject = value + AssetName;
            }
            catch
            {
                throw new Exception("Cannot set PolicyId to Subject");
            }
        }
    }

    [NotMapped]
    public string AssetName 
    {
        get
        {
            try
            {
                return Subject[56..];       
            }
            catch
            {
                throw new Exception("Cannot derived AssetName from Subject");
            }
        }

        set
        {
            try
            {
                Subject = PolicyId + value;
            }
            catch
            {
                throw new Exception("Cannot set AssetName to Subject");
            }
        }
    }
}