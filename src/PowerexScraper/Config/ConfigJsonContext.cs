using System.Text.Json.Serialization;

namespace PowerexScraper.Config;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EndpointsFile))]
public sealed partial class ConfigJsonContext : JsonSerializerContext;
