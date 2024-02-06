using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Cardano.Sync.Data.Models;

public record Value
{
    public ulong Coin { get; init; } = default!;

    [NotMapped]
    public Dictionary<string, Dictionary<string, ulong>> MultiAsset { get; set; } = default!;

    public JsonElement MultiAssetJson
    {
        get
        {
            var jsonString = JsonSerializer.Serialize(MultiAsset);
            return JsonDocument.Parse(jsonString).RootElement;
        }

        set
        {
            if (value.ValueKind == JsonValueKind.Undefined || value.ValueKind == JsonValueKind.Null)
            {
                MultiAsset = [];
            }
            else
            {
                MultiAsset = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, ulong>>>(value.GetRawText()) ?? [];
            }
        }
    }
}