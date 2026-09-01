using System.Globalization;
using System.Text.Json;

namespace PowerexScraper.Flattening;

/// <summary>Normalizes the point-value encodings observed live on 2026-08-31 (spec §3):
/// "2665.90" (MW as string) → verbatim; {"alt":"N/A"|"-"|"n/e"} and null → empty cell.</summary>
public static class CellDecoder
{
    public static string Decode(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.String => cell.GetString() ?? "",
        JsonValueKind.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
        _ => "", // {"alt": …}, null, or anything the API invents next
    };
}
