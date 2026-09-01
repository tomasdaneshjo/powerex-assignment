using System.Text;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PowerexScraper;
using PowerexScraper.Config;
using PowerexScraper.Storage;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace PowerexScraper.IntegrationTests;

public sealed class EndToEndTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly List<(PutObjectRequest Request, byte[] Body)> _puts = [];
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    // anchor 2026-08-31 → forecast dataDate 2026-09-01, actuals dataDate 2026-08-30
    private static readonly ScrapeRequest Anchored = new(null, new DateOnly(2026, 8, 31));

    // The SK-focused e2e tests select their datasets explicitly: the CZ per-unit dataset
    // shares the same route, and a run-all would add ...-cz keys the assertions don't stub for.
    private static readonly ScrapeRequest AnchoredSk = new(
        ["generation-forecast-dayahead", "generation-actual-perunit"], new DateOnly(2026, 8, 31));

    public EndToEndTests()
    {
        _s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r =>
        {
            using var ms = new MemoryStream();
            r.InputStream.CopyTo(ms);
            _puts.Add((r, ms.ToArray()));
        }), Arg.Any<CancellationToken>());
    }

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private void StubRoute(string path, string fixtureName)
        => _server.Given(Request.Create().WithPath(path).UsingPost())
                  .RespondWith(Response.Create().WithStatusCode(200)
                      .WithHeader("Content-Type", "application/json").WithBody(Fixture(fixtureName)));

    private Function BuildFunction(string? endpointsPath = null)
    {
        var env = new Dictionary<string, string?> { ["ENTSOE_BASE_URL"] = _server.Urls[0] };
        var config = AppConfig.Load(k => env.GetValueOrDefault(k), endpointsPath);
        var services = new ServiceCollection().AddScraper(config);
        services.AddSingleton(_s3);
        services.AddSingleton<IObjectStore>(sp => new S3ObjectStore(sp.GetRequiredService<IAmazonS3>(), "test-bucket"));
        return new Function(services.BuildServiceProvider());
    }

    private static ILambdaContext Context()
    {
        var ctx = Substitute.For<ILambdaContext>();
        ctx.Logger.Returns(Substitute.For<ILambdaLogger>());
        return ctx;
    }

    [Fact]
    public async Task Both_real_datasets_flow_end_to_end_into_s3_objects()
    {
        StubRoute("/generation/forecast/dayAhead/load", "prod-dayahead-seps.json");
        StubRoute("/generation/actual/perUnit/load", "prod-perunit-seps.json");

        var summary = await BuildFunction().HandleAsync(AnchoredSk, Context());

        Assert.All(summary.Results, r => Assert.True(r.Success));
        Assert.All(_puts, p => Assert.Equal("test-bucket", p.Request.BucketName));

        var keys = _puts.Select(p => p.Request.Key).ToList();
        Assert.Contains("raw/generation-forecast-dayahead/2026-09-01.json", keys);
        Assert.Contains("data/generation-forecast-dayahead/area=10YSK-SEPS-----K/2026-09-01.csv", keys);
        Assert.Contains("raw/generation-actual-perunit/2026-08-30.json", keys);
        Assert.Contains("data/generation-actual-perunit/area=10YSK-SEPS-----K/2026-08-30.csv", keys);

        var forecastCsv = Encoding.UTF8.GetString(_puts
            .Single(p => p.Request.Key.EndsWith("2026-09-01.csv")).Body).Split("\r\n");
        Assert.Equal("AREA,timestamp_utc,resolution,GENERATION_FORECAST,ACTUAL_GENERATION,SCHEDULED_CONSUMPTION",
                     forecastCsv[0]);
        Assert.Equal("CTA|10YSK-SEPS-----K,2026-08-30T22:00:00Z,PT15M,,2665.90,", forecastCsv[1]);

        var perUnitCsv = Encoding.UTF8.GetString(_puts
            .Single(p => p.Request.Key.EndsWith("2026-08-30.csv")).Body).Split("\r\n");
        Assert.Equal(1 + 23 * 24 + 1, perUnitCsv.Length); // header + rows + trailing newline
        Assert.Contains("Bohunice TG31", perUnitCsv[1]);
    }

    [Fact]
    public async Task Money_test_a_never_coded_dataset_works_on_config_alone()
    {
        StubRoute("/market/prices/dayAhead/PT60M/load", "synthetic-prices-dayahead.json");
        var endpointsPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "endpoints-with-prices.json");

        var summary = await BuildFunction(endpointsPath).HandleAsync(Anchored, Context());

        var result = Assert.Single(summary.Results);
        Assert.True(result.Success);
        Assert.Equal(3, result.RowCount);

        var csv = Encoding.UTF8.GetString(_puts
            .Single(p => p.Request.Key == "data/market-prices-dayahead/area=10YSK-SEPS-----K/2026-09-01.csv")
            .Body).Split("\r\n");
        Assert.Equal("AREA,timestamp_utc,resolution,DAY_AHEAD_PRICE", csv[0]);
        Assert.Equal("BZN|10YSK-SEPS-----K,2026-09-01T22:00:00Z,PT60M,101.10", csv[1]);
        Assert.Equal("BZN|10YSK-SEPS-----K,2026-09-02T00:00:00Z,PT60M,", csv[3]); // the N/A hour
    }

    [Fact]
    public async Task Enrichment_source_and_join_key_are_config_not_code()
    {
        // A synthetic dataset whose lookup table is NOT "supplement" and whose join key is
        // NOT COMPOSITE_EIC — proving the enrichment mechanism is fully declarative.
        StubRoute("/transmission/borderFlows/load", "synthetic-borderflows.json");
        var endpointsPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "endpoints-with-borderflows.json");

        var summary = await BuildFunction(endpointsPath).HandleAsync(Anchored, Context());

        var result = Assert.Single(summary.Results);
        Assert.True(result.Success);
        Assert.Equal(2, result.RowCount);

        var csv = Encoding.UTF8.GetString(_puts
            .Single(p => p.Request.Key == "data/border-flows/area=10YSK-SEPS-----K/2026-09-01.csv")
            .Body).Split("\r\n");
        Assert.Equal("AREA,TIE_LINE,timestamp_utc,resolution,PHYSICAL_FLOW,borderName,voltageLevel", csv[0]);
        Assert.Equal("CTA|10YSK-SEPS-----K,SK-HU-1,2026-08-31T22:00:00Z,PT60M,412.00,SEPS → MAVIR,400", csv[1]);
        Assert.Equal("CTA|10YSK-SEPS-----K,SK-HU-1,2026-08-31T23:00:00Z,PT60M,398.50,SEPS → MAVIR,400", csv[2]);
    }

    [Fact]
    public async Task Transient_500s_are_retried_through_the_real_pipeline()
    {
        StubRoute("/generation/actual/perUnit/load", "prod-perunit-seps.json");
        _server.Given(Request.Create().WithPath("/generation/forecast/dayAhead/load").UsingPost())
               .InScenario("flaky").WillSetStateTo("s1")
               .RespondWith(Response.Create().WithStatusCode(500));
        _server.Given(Request.Create().WithPath("/generation/forecast/dayAhead/load").UsingPost())
               .InScenario("flaky").WhenStateIs("s1")
               .RespondWith(Response.Create().WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json").WithBody(Fixture("prod-dayahead-seps.json")));

        var summary = await BuildFunction().HandleAsync(AnchoredSk, Context());

        Assert.All(summary.Results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task One_dataset_hard_failing_still_lands_the_other_then_throws()
    {
        // 404 fails fast (no retry) — isolation is what's under test, not backoff
        _server.Given(Request.Create().WithPath("/generation/forecast/dayAhead/load").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(404));
        StubRoute("/generation/actual/perUnit/load", "prod-perunit-seps.json");

        var ex = await Assert.ThrowsAsync<ScrapeFailedException>(
            () => BuildFunction().HandleAsync(AnchoredSk, Context()));

        Assert.False(ex.Summary.Results.Single(r => r.DatasetId == "generation-forecast-dayahead").Success);
        Assert.True(ex.Summary.Results.Single(r => r.DatasetId == "generation-actual-perunit").Success);
        Assert.Contains(_puts, p => p.Request.Key == "data/generation-actual-perunit/area=10YSK-SEPS-----K/2026-08-30.csv");
    }

    public void Dispose() => _server.Stop();
}
