using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using PowerexScraper.Config;
using PowerexScraper.Entsoe;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace PowerexScraper.Tests;

public sealed class EntsoeClientTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private static readonly DatasetConfig Dataset = new(
        "generation-forecast-dayahead", "generation/forecast/dayAhead",
        ["CTA|10YSK-SEPS-----K"], "CET",
        new WindowSpec("Europe/Bratislava", 1, 1), null);

    private static readonly ScrapeWindow Window = new(
        new DateTimeOffset(2026, 8, 31, 22, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 1, 22, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 9, 1));

    private const string MinimalEnvelope =
        """{"uuAppErrorMap":{},"instanceList":[],"metaData":[]}""";

    private IEntsoeClient BuildClient()
        => new ServiceCollection()
            .AddEntsoeClient(new Uri(_server.Urls[0]))
            .BuildServiceProvider()
            .GetRequiredService<IEntsoeClient>();

    [Fact]
    public async Task Posts_verified_dtoIn_to_route_load()
    {
        _server.Given(Request.Create().WithPath("/generation/forecast/dayAhead/load").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(MinimalEnvelope));

        var response = await BuildClient().FetchAsync(Dataset, Window);

        Assert.Equal(System.Text.Json.JsonValueKind.Object, response.Document.RootElement.ValueKind);
        var entry = Assert.Single(_server.LogEntries);
        var body = JsonNode.Parse(entry.RequestMessage!.Body!)!;
        Assert.Equal("2026-08-31T22:00:00.000Z", (string)body["dateTimeRange"]!["from"]!);
        Assert.Equal("2026-09-01T22:00:00.000Z", (string)body["dateTimeRange"]!["to"]!);
        Assert.Equal("CTA|10YSK-SEPS-----K", (string)body["areaList"]![0]!);
        Assert.Equal("CET", (string)body["timeZone"]!);
        Assert.Equal("application/json", entry.RequestMessage.Headers!["Content-Type"][0].Split(';')[0]);
        Assert.StartsWith("powerex-scraper/", entry.RequestMessage.Headers!["User-Agent"][0]);
    }

    [Fact]
    public async Task Retries_transient_500s_then_succeeds()
    {
        _server.Given(Request.Create().WithPath("/generation/forecast/dayAhead/load").UsingPost())
               .InScenario("retry").WillSetStateTo("failed-once")
               .RespondWith(Response.Create().WithStatusCode(500));
        _server.Given(Request.Create().WithPath("/generation/forecast/dayAhead/load").UsingPost())
               .InScenario("retry").WhenStateIs("failed-once").WillSetStateTo("failed-twice")
               .RespondWith(Response.Create().WithStatusCode(500));
        _server.Given(Request.Create().WithPath("/generation/forecast/dayAhead/load").UsingPost())
               .InScenario("retry").WhenStateIs("failed-twice")
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(MinimalEnvelope));

        var response = await BuildClient().FetchAsync(Dataset, Window);

        Assert.NotNull(response.Document);
        Assert.Equal(3, _server.LogEntries.Count());
    }

    [Fact]
    public async Task Client_error_400_is_not_retried()
    {
        _server.Given(Request.Create().WithPath("/generation/forecast/dayAhead/load").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(400));

        await Assert.ThrowsAsync<HttpRequestException>(() => BuildClient().FetchAsync(Dataset, Window));
        Assert.Single(_server.LogEntries);
    }

    public void Dispose() => _server.Stop();
}
