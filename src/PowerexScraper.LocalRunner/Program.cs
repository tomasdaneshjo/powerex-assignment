using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using PowerexScraper;
using PowerexScraper.Config;
using PowerexScraper.Storage;

string? dataset = null, date = null, baseUrl = null, outDir = "out";
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dataset" or "--date" or "--base-url" or "--out"
            when i + 1 >= args.Length || args[i + 1].StartsWith("--"):
            Console.Error.WriteLine($"Missing value for '{args[i]}'. Try --help.");
            return 2;
        case "--dataset": dataset = args[++i]; break;
        case "--date": date = args[++i]; break;
        case "--base-url": baseUrl = args[++i]; break;
        case "--out": outDir = args[++i]; break;
        case "--help" or "-h":
            Console.WriteLine("""
                Runs the ENTSO-E scrape pipeline locally against the LIVE API.
                Usage: dotnet run --project src/PowerexScraper.LocalRunner -- [options]
                  --dataset <id>      one dataset id from endpoints.json (default: all)
                  --date yyyy-MM-dd   anchor date override (default: today)
                  --base-url <url>    default https://iop-transparency.entsoe.eu
                  --out <dir>         output directory (default: ./out)
                """);
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'. Try --help.");
            return 2;
    }
}

var env = new Dictionary<string, string?> { ["ENTSOE_BASE_URL"] = baseUrl };
var config = AppConfig.Load(k => env.GetValueOrDefault(k) ?? Environment.GetEnvironmentVariable(k));

var services = new ServiceCollection().AddScraper(config);
services.AddSingleton<IObjectStore>(new FileSystemObjectStore(outDir));
await using var provider = services.BuildServiceProvider();

var datasetIds = dataset is null ? null : new[] { dataset };
DateOnly? dateOverride = null;
if (date is not null)
{
    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsedDate))
    {
        Console.Error.WriteLine($"Invalid --date '{date}' — expected yyyy-MM-dd. Try --help.");
        return 2;
    }
    dateOverride = parsedDate;
}

try
{
    var summary = await provider.GetRequiredService<ScrapeOrchestrator>()
        .RunAsync(datasetIds, dateOverride, new ConsoleLogger());
    foreach (var r in summary.Results)
        Console.WriteLine($"OK  {r.DatasetId}: {r.RowCount} rows ({r.EmptyCellCount} empty cells) → {outDir}/");
    return 0;
}
catch (ScrapeFailedException ex)
{
    foreach (var r in ex.Summary.Results)
        Console.WriteLine(r.Success
            ? $"OK   {r.DatasetId}: {r.RowCount} rows"
            : $"FAIL {r.DatasetId}: {r.Error}");
    return 1;
}

internal sealed class ConsoleLogger : ILambdaLogger
{
    public void Log(string message) => Console.Write(message);
    public void LogLine(string message) => Console.WriteLine(message);
}
