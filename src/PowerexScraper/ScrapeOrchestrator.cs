using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using PowerexScraper.Config;
using PowerexScraper.Entsoe;
using PowerexScraper.Flattening;
using PowerexScraper.Storage;

namespace PowerexScraper;

public sealed record DatasetRunResult(string DatasetId, bool Success, int RowCount, int EmptyCellCount, string? Error);

public sealed record RunSummary(IReadOnlyList<DatasetRunResult> Results);

/// <summary>Thrown after ALL selected datasets were attempted, so successful datasets' objects
/// are already in S3; the throw makes the invocation register in the Lambda Errors metric and
/// and Lambda's async-invocation retry re-runs the invocation (spec §8 addendum).</summary>
public sealed class ScrapeFailedException(RunSummary summary)
    : Exception($"{summary.Results.Count(r => !r.Success)} of {summary.Results.Count} datasets failed: "
                + string.Join("; ", summary.Results.Where(r => !r.Success).Select(r => $"{r.DatasetId}: {r.Error}")))
{
    public RunSummary Summary { get; } = summary;
}

public sealed class ScrapeOrchestrator(
    IEntsoeClient client,
    WindowCalculator windows,
    IObjectStore store,
    AppConfig config)
{
    public async Task<RunSummary> RunAsync(
        IReadOnlyList<string>? datasetIds,
        DateOnly? dateOverride,
        ILambdaLogger logger,
        CancellationToken ct = default)
    {
        var results = new List<DatasetRunResult>();

        var selected = datasetIds is null or []
            ? config.Datasets.Select(d => (Id: d.Id, Dataset: (DatasetConfig?)d))
            : datasetIds.Select(id => (Id: id, Dataset: config.Datasets.FirstOrDefault(d => d.Id == id)));

        foreach (var (id, dataset) in selected)
        {
            if (dataset is null)
            {
                results.Add(new DatasetRunResult(id, false, 0, 0, $"Unknown dataset id '{id}'"));
                continue;
            }
            try
            {
                results.Add(await RunDatasetAsync(dataset, dateOverride, logger, ct));
            }
            catch (Exception ex)
            {
                var detail = ex is EntsoeContractException { BodyExcerpt: not null } contract
                    ? $"{ex.Message} | body: {contract.BodyExcerpt}"
                    : ex.Message;
                logger.LogError($"dataset={dataset.Id} FAILED: {detail}");
                results.Add(new DatasetRunResult(dataset.Id, false, 0, 0, ex.Message));
            }
        }

        var summary = new RunSummary(results);
        return results.Any(r => !r.Success) ? throw new ScrapeFailedException(summary) : summary;
    }

    private async Task<DatasetRunResult> RunDatasetAsync(
        DatasetConfig dataset, DateOnly? dateOverride, ILambdaLogger logger, CancellationToken ct)
    {
        var window = windows.Calculate(dataset.Window, dateOverride);
        var response = await client.FetchAsync(dataset, window, ct);
        using var doc = response.Document;

        // Archived before validation: even a response that fails the contract check is
        // preserved for offline diagnosis (spec §4 — raw JSON is evidence, not just an input).
        await store.PutAsync(KeyBuilder.RawKey(dataset.Id, window.DataDate),
                             response.RawBytes, "application/json", ct);

        FlattenResult flat;
        try
        {
            EnvelopeValidator.Validate(doc);
            flat = CurveFlattener.Flatten(doc);
            if (dataset.Enrichment is not null)
                flat = SupplementEnricher.Enrich(flat, doc, dataset.Enrichment);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException
                                       or FormatException or JsonException)
        {
            // The flattener/enricher assume the envelope shape holds; when it doesn't, turn
            // whatever BCL exception that produced into a named, diagnosable contract failure
            // instead of letting a raw KeyNotFoundException surface to the caller (spec §8).
            var raw = Encoding.UTF8.GetString(response.RawBytes);
            var excerpt = raw.Length <= 2000 ? raw : raw[..2000];
            throw new EntsoeContractException(
                $"Response structure did not match the expected envelope: {ex.Message}", excerpt);
        }

        // intervalPageInfo is not part of the validated envelope — tolerate its absence.
        if (doc.RootElement.TryGetProperty("intervalPageInfo", out var pageInfo)
            && pageInfo.TryGetProperty("pageSize", out var pageSizeEl)
            && pageInfo.TryGetProperty("total", out var totalEl))
        {
            var pageSize = pageSizeEl.GetInt32();
            var total = totalEl.GetInt32();
            if (total > pageSize)
                logger.LogWarning(
                    $"dataset={dataset.Id} intervalPageInfo.total={total} exceeds pageSize={pageSize} " +
                    "— response may be paged; rows beyond page 1 are not fetched");
        }

        var fallbackArea = dataset.Areas.Count > 0 ? dataset.Areas[0] : "unknown";
        foreach (var areaGroup in flat.Rows.GroupBy(r => r.Dimensions.GetValueOrDefault("AREA", fallbackArea)))
        {
            var areaResult = flat with { Rows = areaGroup.ToList() };
            var csv = Csv.CsvSerializer.Serialize(areaResult);
            await store.PutAsync(KeyBuilder.CsvKey(dataset.Id, areaGroup.Key, window.DataDate),
                                 Encoding.UTF8.GetBytes(csv), "text/csv", ct);
        }

        var emptyCells = flat.Rows.Sum(r =>
            r.Values.Values.Count(v => v == "") + r.Enrichment.Values.Count(v => v == ""));
        // "succeeded but empty" must be visible in the logs (spec §8)
        logger.LogInformation(
            $"dataset={dataset.Id} dataDate={window.DataDate:yyyy-MM-dd} rows={flat.Rows.Count} emptyCells={emptyCells}");
        return new DatasetRunResult(dataset.Id, true, flat.Rows.Count, emptyCells, null);
    }
}
