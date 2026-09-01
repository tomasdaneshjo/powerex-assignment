using System.Globalization;
using CsvHelper;
using PowerexScraper.Flattening;

namespace PowerexScraper.Csv;

/// <summary>FlattenResult → RFC-4180 CSV. Column order (spec §6): dimensions (alphabetical,
/// already ordered by the flattener) → timestamp_utc, resolution → metaData codes (response
/// order) → enrichment columns (config order).</summary>
public static class CsvSerializer
{
    public static string Serialize(FlattenResult result)
    {
        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        foreach (var column in result.DimensionColumns) csv.WriteField(column);
        csv.WriteField("timestamp_utc");
        csv.WriteField("resolution");
        foreach (var column in result.ValueColumns) csv.WriteField(column);
        foreach (var column in result.EnrichmentColumns) csv.WriteField(column);
        csv.NextRecord();

        foreach (var row in result.Rows)
        {
            foreach (var column in result.DimensionColumns)
                csv.WriteField(row.Dimensions.GetValueOrDefault(column, ""));
            csv.WriteField(row.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
                                                                CultureInfo.InvariantCulture));
            csv.WriteField(row.Resolution);
            foreach (var column in result.ValueColumns)
                csv.WriteField(row.Values.GetValueOrDefault(column, ""));
            foreach (var column in result.EnrichmentColumns)
                csv.WriteField(row.Enrichment.GetValueOrDefault(column, ""));
            csv.NextRecord();
        }

        csv.Flush();
        return writer.ToString();
    }
}
