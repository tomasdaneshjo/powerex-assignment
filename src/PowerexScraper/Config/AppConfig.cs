using System.Text.Json;

namespace PowerexScraper.Config;

public sealed record WindowSpec(string AnchorTimeZone, int StartOffsetDays, int DurationDays);

public sealed record EnrichmentSpec(string Source, string InstanceKeyAttribute, IReadOnlyList<string> Columns);

public sealed record DatasetConfig(
    string Id,
    string RoutePath,
    IReadOnlyList<string> Areas,
    string TimeZone,
    WindowSpec Window,
    EnrichmentSpec? Enrichment);

public sealed record EndpointsFile(IReadOnlyList<DatasetConfig> Datasets);

public sealed record AppConfig(Uri BaseUrl, string OutputBucket, IReadOnlyList<DatasetConfig> Datasets)
{
    public const string DefaultBaseUrl = "https://iop-transparency.entsoe.eu";

    /// <summary>Loads endpoints.json (packaged next to the assembly) + env-var overlay.</summary>
    public static AppConfig Load(Func<string, string?> getEnv, string? endpointsPath = null)
    {
        endpointsPath ??= Path.Combine(AppContext.BaseDirectory, "endpoints.json");
        using var stream = File.OpenRead(endpointsPath);
        var file = JsonSerializer.Deserialize(stream, ConfigJsonContext.Default.EndpointsFile)
                   ?? throw new InvalidOperationException($"endpoints.json at '{endpointsPath}' deserialized to null.");

        if (file.Datasets.Count == 0)
            throw new InvalidOperationException("endpoints.json contains no datasets.");

        // The source generator fills a missing JSON property with null even though the record's
        // properties are declared non-nullable — validate explicitly instead of letting a null
        // ripple into a NullReferenceException somewhere downstream.
        for (var i = 0; i < file.Datasets.Count; i++)
        {
            var d = file.Datasets[i];
            var idOrIndex = string.IsNullOrEmpty(d.Id) ? $"#{i}" : d.Id;
            if (string.IsNullOrEmpty(d.Id))
                throw new InvalidOperationException($"Dataset '{idOrIndex}': missing required field 'id'");
            if (string.IsNullOrEmpty(d.RoutePath))
                throw new InvalidOperationException($"Dataset '{idOrIndex}': missing required field 'routePath'");
            if (string.IsNullOrEmpty(d.TimeZone))
                throw new InvalidOperationException($"Dataset '{idOrIndex}': missing required field 'timeZone'");
            if (d.Areas is null or [])
                throw new InvalidOperationException($"Dataset '{idOrIndex}': missing required field 'areas'");
            if (d.Window is null)
                throw new InvalidOperationException($"Dataset '{idOrIndex}': missing required field 'window'");
        }

        var duplicates = file.Datasets.GroupBy(d => d.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException($"Duplicate dataset ids: {string.Join(", ", duplicates)}");
        foreach (var d in file.Datasets)
            if (d.RoutePath.StartsWith('/') || d.RoutePath.EndsWith('/'))
                throw new InvalidOperationException($"Dataset '{d.Id}': routePath must not start or end with '/'.");

        var baseUrl = new Uri(getEnv("ENTSOE_BASE_URL") ?? DefaultBaseUrl);
        var bucket = getEnv("OUTPUT_BUCKET") ?? "";
        return new AppConfig(baseUrl, bucket, file.Datasets);
    }
}
