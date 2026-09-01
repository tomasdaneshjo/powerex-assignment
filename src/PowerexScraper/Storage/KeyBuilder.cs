using System.Globalization;

namespace PowerexScraper.Storage;

/// <summary>Deterministic S3 keys carrying the DATA date (never the run date) — a retry or
/// manual re-run overwrites the same object: idempotency by key design (spec §4).</summary>
public static class KeyBuilder
{
    public static string CsvKey(string datasetId, string areaToken, DateOnly dataDate)
    {
        var eic = areaToken.Contains('|') ? areaToken[(areaToken.IndexOf('|') + 1)..] : areaToken;
        return $"data/{datasetId}/area={eic}/{dataDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";
    }

    public static string RawKey(string datasetId, DateOnly dataDate)
        => $"raw/{datasetId}/{dataDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json";
}
