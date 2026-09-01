using System.Text.Json;
using PowerexScraper.Config;

namespace PowerexScraper.Flattening;

/// <summary>Config-driven join: instanceAttributeMap[key] → dtoOut[source] → configured columns.
/// Tolerant by design — a missing supplement entry or field is an empty cell, never a failure.</summary>
public static class SupplementEnricher
{
    public static FlattenResult Enrich(FlattenResult result, JsonDocument doc, EnrichmentSpec spec)
    {
        doc.RootElement.TryGetProperty(spec.Source, out var supplement);

        var enrichedRows = result.Rows.Select(row =>
        {
            var enrichment = new Dictionary<string, string>(spec.Columns.Count);
            JsonElement entry = default;
            var found = supplement.ValueKind == JsonValueKind.Object
                        && row.InstanceAttributes.TryGetValue(spec.InstanceKeyAttribute, out var key)
                        && supplement.TryGetProperty(key, out entry)
                        && entry.ValueKind == JsonValueKind.Object;

            foreach (var column in spec.Columns)
                enrichment[column] = found
                                     && entry.TryGetProperty(column, out var value)
                                     && value.ValueKind == JsonValueKind.String
                    ? value.GetString()!
                    : "";

            return row with { Enrichment = enrichment };
        }).ToList();

        return result with { EnrichmentColumns = spec.Columns, Rows = enrichedRows };
    }
}
