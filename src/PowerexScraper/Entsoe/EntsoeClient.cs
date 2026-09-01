using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PowerexScraper.Config;

namespace PowerexScraper.Entsoe;

public sealed record EntsoeResponse(byte[] RawBytes, JsonDocument Document);

public interface IEntsoeClient
{
    Task<EntsoeResponse> FetchAsync(DatasetConfig dataset, ScrapeWindow window, CancellationToken ct = default);
}

/// <summary>POSTs the SPA's internal command: {baseUrl}/{routePath}/load with the minimal dtoIn
/// verified live on 2026-08-31 (spec §3). No auth — Content-Type is the only required header.</summary>
public sealed class EntsoeClient(HttpClient http) : IEntsoeClient
{
    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public async Task<EntsoeResponse> FetchAsync(DatasetConfig dataset, ScrapeWindow window, CancellationToken ct = default)
    {
        var dtoIn = new DtoIn(
            new DtoRange(
                window.FromUtc.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture),
                window.ToUtc.UtcDateTime.ToString(DateFormat, CultureInfo.InvariantCulture)),
            dataset.Areas,
            dataset.TimeZone);

        using var response = await http.PostAsJsonAsync(
            $"{dataset.RoutePath}/load", dtoIn, EntsoeJsonContext.Default.DtoIn, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        return new EntsoeResponse(bytes, JsonDocument.Parse(bytes));
    }
}
