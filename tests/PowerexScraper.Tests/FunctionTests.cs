using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PowerexScraper.Config;
using PowerexScraper.Entsoe;
using PowerexScraper.Storage;

namespace PowerexScraper.Tests;

public class FunctionTests
{
    private sealed class InMemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Objects = [];
        public Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
        {
            Objects[key] = content;
            return Task.CompletedTask;
        }
    }

    private static (Function Function, InMemoryStore Store) Build()
    {
        var config = AppConfig.Load(_ => null);
        var store = new InMemoryStore();
        var client = Substitute.For<IEntsoeClient>();
        client.FetchAsync(Arg.Any<DatasetConfig>(), Arg.Any<ScrapeWindow>(), Arg.Any<CancellationToken>())
              .Returns(_ =>
              {
                  var bytes = File.ReadAllBytes(
                      Path.Combine(AppContext.BaseDirectory, "Fixtures", "prod-dayahead-seps.json"));
                  return new EntsoeResponse(bytes, System.Text.Json.JsonDocument.Parse(bytes));
              });

        var services = new ServiceCollection().AddScraper(config);
        services.AddSingleton<IObjectStore>(store);
        services.AddSingleton(client);                     // replaces the typed-client registration
        return (new Function(services.BuildServiceProvider()), store);
    }

    private static ILambdaContext Context()
    {
        var ctx = Substitute.For<ILambdaContext>();
        ctx.Logger.Returns(Substitute.For<ILambdaLogger>());
        return ctx;
    }

    [Fact]
    public async Task Null_request_runs_all_configured_datasets()
    {
        var (function, _) = Build();
        var summary = await function.HandleAsync(null, Context());
        Assert.Equal(3, summary.Results.Count);
        Assert.All(summary.Results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task Request_with_dataset_ids_runs_only_those()
    {
        var (function, store) = Build();
        var summary = await function.HandleAsync(new ScrapeRequest(["generation-forecast-dayahead"]), Context());
        Assert.Single(summary.Results);
        Assert.All(store.Objects.Keys, k => Assert.Contains("generation-forecast-dayahead", k));
    }
}
