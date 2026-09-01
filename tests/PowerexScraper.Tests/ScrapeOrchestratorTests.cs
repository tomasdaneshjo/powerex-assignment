using Amazon.Lambda.Core;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PowerexScraper.Config;
using PowerexScraper.Entsoe;
using PowerexScraper.Storage;

namespace PowerexScraper.Tests;

public class ScrapeOrchestratorTests
{
    private sealed class FixedClock : IClock
    {
        // 2026-08-31 17:30 CEST → forecast dataDate 2026-09-01, actuals dataDate 2026-08-30
        public DateTimeOffset UtcNow => new(2026, 8, 31, 15, 30, 0, TimeSpan.Zero);
    }

    private sealed class InMemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Objects = [];
        public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
        {
            Objects[key] = content;
            return Task.CompletedTask;
        }
    }

    // A real class, not an NSubstitute mock: LogWarning/LogInformation/LogError are default
    // interface methods on ILambdaLogger that route down to Log/LogLine — an NSubstitute
    // substitute implements every interface member directly and never exercises that routing,
    // so it can't prove a call to logger.LogWarning(...) actually produced a captured line.
    private sealed class CapturingLogger : ILambdaLogger
    {
        public readonly List<string> Lines = [];
        public void Log(string message) => Lines.Add(message);
        public void LogLine(string message) => Lines.Add(message);
    }

    private static readonly ILambdaLogger Logger = Substitute.For<ILambdaLogger>();

    private static AppConfig Config() =>
        AppConfig.Load(_ => null); // packaged endpoints.json: both real datasets

    private static EntsoeResponse ResponseFromFixture(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return new EntsoeResponse(bytes, System.Text.Json.JsonDocument.Parse(bytes));
    }

    private static EntsoeResponse ResponseFromJson(string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return new EntsoeResponse(bytes, System.Text.Json.JsonDocument.Parse(bytes));
    }

    [Fact]
    public async Task Happy_path_stores_raw_json_and_per_area_csv_under_data_date_keys()
    {
        var client = Substitute.For<IEntsoeClient>();
        client.FetchAsync(Arg.Any<DatasetConfig>(), Arg.Any<ScrapeWindow>(), Arg.Any<CancellationToken>())
              .Returns(_ => ResponseFromFixture("prod-dayahead-seps.json"));
        var store = new InMemoryStore();
        var orchestrator = new ScrapeOrchestrator(client, new WindowCalculator(new FixedClock()), store, Config());

        var summary = await orchestrator.RunAsync(["generation-forecast-dayahead"], null, Logger);

        var result = Assert.Single(summary.Results);
        Assert.True(result.Success);
        Assert.Equal(96, result.RowCount);

        Assert.Contains("raw/generation-forecast-dayahead/2026-09-01.json", store.Objects.Keys);
        var csvKey = "data/generation-forecast-dayahead/area=10YSK-SEPS-----K/2026-09-01.csv";
        Assert.Contains(csvKey, store.Objects.Keys);
        var firstLine = System.Text.Encoding.UTF8.GetString(store.Objects[csvKey]).Split("\r\n")[0];
        Assert.Equal("AREA,timestamp_utc,resolution,GENERATION_FORECAST,ACTUAL_GENERATION,SCHEDULED_CONSUMPTION",
                     firstLine);
    }

    [Fact]
    public async Task PerUnit_dataset_applies_enrichment_columns()
    {
        var client = Substitute.For<IEntsoeClient>();
        client.FetchAsync(Arg.Any<DatasetConfig>(), Arg.Any<ScrapeWindow>(), Arg.Any<CancellationToken>())
              .Returns(_ => ResponseFromFixture("prod-perunit-seps.json"));
        var store = new InMemoryStore();
        var orchestrator = new ScrapeOrchestrator(client, new WindowCalculator(new FixedClock()), store, Config());

        await orchestrator.RunAsync(["generation-actual-perunit"], null, Logger);

        var csvKey = "data/generation-actual-perunit/area=10YSK-SEPS-----K/2026-08-30.csv";
        var header = System.Text.Encoding.UTF8.GetString(store.Objects[csvKey]).Split("\r\n")[0];
        Assert.Equal("AREA,GENERATION_UNIT,PRODUCTION_TYPE,timestamp_utc,resolution," +
                     "ACTUAL_GENERATION_OUTPUT,ACTUAL_CONSUMPTION,unitName,productionType,installedCapacity",
                     header);
    }

    [Fact]
    public async Task One_failing_dataset_does_not_block_the_other_but_fails_the_run()
    {
        var client = Substitute.For<IEntsoeClient>();
        client.FetchAsync(Arg.Is<DatasetConfig>(d => d.Id == "generation-forecast-dayahead"),
                          Arg.Any<ScrapeWindow>(), Arg.Any<CancellationToken>())
              .ThrowsAsync(new HttpRequestException("boom"));
        client.FetchAsync(Arg.Is<DatasetConfig>(d => d.Id == "generation-actual-perunit"),
                          Arg.Any<ScrapeWindow>(), Arg.Any<CancellationToken>())
              .Returns(_ => ResponseFromFixture("prod-perunit-seps.json"));
        var store = new InMemoryStore();
        var orchestrator = new ScrapeOrchestrator(client, new WindowCalculator(new FixedClock()), store, Config());

        var ex = await Assert.ThrowsAsync<ScrapeFailedException>(
            () => orchestrator.RunAsync(["generation-forecast-dayahead", "generation-actual-perunit"], null, Logger));

        Assert.Equal(2, ex.Summary.Results.Count);
        Assert.False(ex.Summary.Results.Single(r => r.DatasetId == "generation-forecast-dayahead").Success);
        Assert.True(ex.Summary.Results.Single(r => r.DatasetId == "generation-actual-perunit").Success);
        Assert.Contains("data/generation-actual-perunit/area=10YSK-SEPS-----K/2026-08-30.csv",
                        store.Objects.Keys); // the good dataset still landed
    }

    [Fact]
    public async Task Unknown_dataset_id_is_a_failed_result_without_a_fetch()
    {
        var client = Substitute.For<IEntsoeClient>();
        var orchestrator = new ScrapeOrchestrator(
            client, new WindowCalculator(new FixedClock()), new InMemoryStore(), Config());

        var ex = await Assert.ThrowsAsync<ScrapeFailedException>(
            () => orchestrator.RunAsync(["nope"], null, Logger));

        var result = Assert.Single(ex.Summary.Results);
        Assert.False(result.Success);
        Assert.Contains("nope", result.Error);
        await client.DidNotReceiveWithAnyArgs().FetchAsync(default!, default!, default);
    }

    [Fact]
    public async Task Date_override_shifts_the_data_date_keys()
    {
        var client = Substitute.For<IEntsoeClient>();
        client.FetchAsync(Arg.Any<DatasetConfig>(), Arg.Any<ScrapeWindow>(), Arg.Any<CancellationToken>())
              .Returns(_ => ResponseFromFixture("prod-dayahead-seps.json"));
        var store = new InMemoryStore();
        var orchestrator = new ScrapeOrchestrator(client, new WindowCalculator(new FixedClock()), store, Config());

        await orchestrator.RunAsync(["generation-forecast-dayahead"], new DateOnly(2026, 6, 10), Logger);

        Assert.Contains("raw/generation-forecast-dayahead/2026-06-11.json", store.Objects.Keys);
    }

    [Fact]
    public async Task IntervalPageInfo_total_exceeding_pageSize_logs_a_paging_warning()
    {
        var client = Substitute.For<IEntsoeClient>();
        client.FetchAsync(Arg.Any<DatasetConfig>(), Arg.Any<ScrapeWindow>(), Arg.Any<CancellationToken>())
              .Returns(_ => ResponseFromJson(
                  """{"uuAppErrorMap":{},"instanceList":[],"metaData":[],"intervalPageInfo":{"pageSize":1,"total":5}}"""));
        var store = new InMemoryStore();
        var logger = new CapturingLogger();
        var orchestrator = new ScrapeOrchestrator(client, new WindowCalculator(new FixedClock()), store, Config());

        await orchestrator.RunAsync(["generation-forecast-dayahead"], null, logger);

        Assert.Contains(logger.Lines, l => l.Contains("exceeds pageSize"));
    }

    [Fact]
    public async Task Invalid_envelope_still_archives_the_raw_response_before_failing()
    {
        var client = Substitute.For<IEntsoeClient>();
        client.FetchAsync(Arg.Any<DatasetConfig>(), Arg.Any<ScrapeWindow>(), Arg.Any<CancellationToken>())
              .Returns(_ => ResponseFromJson("""{"uuAppErrorMap":{},"metaData":[]}""")); // no instanceList
        var store = new InMemoryStore();
        var orchestrator = new ScrapeOrchestrator(client, new WindowCalculator(new FixedClock()), store, Config());

        var ex = await Assert.ThrowsAsync<ScrapeFailedException>(
            () => orchestrator.RunAsync(["generation-forecast-dayahead"], null, Logger));

        var result = Assert.Single(ex.Summary.Results);
        Assert.False(result.Success);
        Assert.Contains("raw/generation-forecast-dayahead/2026-09-01.json", store.Objects.Keys);
    }

    [Fact]
    public async Task Structural_drift_becomes_a_typed_contract_failure_not_a_raw_bcl_exception()
    {
        // Valid envelope shape (passes EnvelopeValidator), but a period is missing "resolution" —
        // CurveFlattener.Flatten hits that via JsonElement.GetProperty, which throws a raw
        // KeyNotFoundException with no mention of "envelope" or "structure".
        var client = Substitute.For<IEntsoeClient>();
        client.FetchAsync(Arg.Any<DatasetConfig>(), Arg.Any<ScrapeWindow>(), Arg.Any<CancellationToken>())
              .Returns(_ => ResponseFromJson(
                  """
                  {"uuAppErrorMap":{},"metaData":[{"code":"VALUE"}],"instanceList":[
                    {"businessDimensionMap":{"AREA":"CTA|X"},"instanceAttributeMap":{},
                     "curveData":{"periodList":[
                       {"timeInterval":{"from":"2026-09-01T00:00:00Z"},"pointMap":{"0":["1.0"]}}
                     ]}}
                  ]}
                  """));
        var store = new InMemoryStore();
        var orchestrator = new ScrapeOrchestrator(client, new WindowCalculator(new FixedClock()), store, Config());

        var ex = await Assert.ThrowsAsync<ScrapeFailedException>(
            () => orchestrator.RunAsync(["generation-forecast-dayahead"], null, Logger));

        var result = Assert.Single(ex.Summary.Results);
        Assert.False(result.Success);
        Assert.Contains("envelope", result.Error);
        Assert.DoesNotContain("was not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
