using System.Text.Json.Serialization;

namespace PowerexScraper.Entsoe;

internal sealed record DtoRange(string From, string To);

internal sealed record DtoIn(DtoRange DateTimeRange, IReadOnlyList<string> AreaList, string TimeZone);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DtoIn))]
internal sealed partial class EntsoeJsonContext : JsonSerializerContext;
