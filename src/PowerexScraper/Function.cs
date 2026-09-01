using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using PowerexScraper.Config;
using PowerexScraper.Storage;
using System.Text.Json.Serialization;

[assembly: LambdaSerializer(typeof(SourceGeneratorLambdaJsonSerializer<PowerexScraper.LambdaJsonContext>))]

namespace PowerexScraper;

/// <summary>Lambda input. Scheduler payloads select datasets; DateOverride replaces "today"
/// as the window anchor, so a manual invoke backfills any day through the same code path.</summary>
public sealed record ScrapeRequest(string[]? DatasetIds = null, DateOnly? DateOverride = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScrapeRequest))]
[JsonSerializable(typeof(RunSummary))]
public sealed partial class LambdaJsonContext : JsonSerializerContext;

public sealed class Function
{
    private readonly IServiceProvider _services;

    /// <summary>Production wiring; runs once per cold start.</summary>
    public Function()
    {
        var config = AppConfig.Load(Environment.GetEnvironmentVariable);
        if (string.IsNullOrEmpty(config.OutputBucket))
            throw new InvalidOperationException("OUTPUT_BUCKET environment variable is required.");

        var services = new ServiceCollection().AddScraper(config);
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
        services.AddSingleton<IObjectStore>(
            sp => new S3ObjectStore(sp.GetRequiredService<IAmazonS3>(), config.OutputBucket));
        _services = services.BuildServiceProvider();
    }

    internal Function(IServiceProvider services) => _services = services;

    public async Task<RunSummary> HandleAsync(ScrapeRequest? request, ILambdaContext context)
    {
        // Cancel the pipeline with headroom before Lambda kills the invocation outright, so an
        // in-flight HTTP call/S3 put can unwind cleanly instead of being hard-terminated.
        var remaining = context.RemainingTime;
        using var cts = remaining > TimeSpan.FromSeconds(10)
            ? new CancellationTokenSource(remaining - TimeSpan.FromSeconds(5))
            : null;
        return await _services.GetRequiredService<ScrapeOrchestrator>()
                              .RunAsync(request?.DatasetIds, request?.DateOverride, context.Logger,
                                        cts?.Token ?? CancellationToken.None);
    }
}
