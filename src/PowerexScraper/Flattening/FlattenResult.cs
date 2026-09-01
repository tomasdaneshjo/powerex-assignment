namespace PowerexScraper.Flattening;

public sealed record Row(
    IReadOnlyDictionary<string, string> Dimensions,
    IReadOnlyDictionary<string, string> InstanceAttributes,
    DateTimeOffset TimestampUtc,
    string Resolution,
    IReadOnlyDictionary<string, string> Values)
{
    public IReadOnlyDictionary<string, string> Enrichment { get; init; } =
        System.Collections.Immutable.ImmutableDictionary<string, string>.Empty;
}

public sealed record FlattenResult(
    IReadOnlyList<string> DimensionColumns,
    IReadOnlyList<string> ValueColumns,
    IReadOnlyList<string> EnrichmentColumns,
    IReadOnlyList<Row> Rows);
