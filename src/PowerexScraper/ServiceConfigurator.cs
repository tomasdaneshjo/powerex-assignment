using Microsoft.Extensions.DependencyInjection;
using PowerexScraper.Config;
using PowerexScraper.Entsoe;

namespace PowerexScraper;

/// <summary>One wiring, three hosts: Lambda (S3 store), LocalRunner (filesystem store),
/// tests (in-memory store). The IObjectStore registration is the host's responsibility.</summary>
public static class ServiceConfigurator
{
    public static IServiceCollection AddScraper(this IServiceCollection services, AppConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<WindowCalculator>();
        services.AddEntsoeClient(config.BaseUrl);
        // Transient, not singleton: the typed IEntsoeClient is transient by design
        // (IHttpClientFactory handler rotation) — a singleton orchestrator would capture one
        // client for the container's life, the documented anti-pattern.
        services.AddTransient<ScrapeOrchestrator>();
        return services;
    }
}
