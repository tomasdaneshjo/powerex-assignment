using Microsoft.Extensions.DependencyInjection;

namespace PowerexScraper.Entsoe;

public static class EntsoeServiceCollectionExtensions
{
    public static IServiceCollection AddEntsoeClient(this IServiceCollection services, Uri baseUrl)
    {
        // BaseAddress must end with '/' for relative route paths to resolve correctly.
        var normalized = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");

        services.AddHttpClient<IEntsoeClient, EntsoeClient>(client =>
        {
            client.BaseAddress = normalized;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("powerex-scraper/1.0");
        })
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;                                  // on 408/429/5xx + transport errors
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);      // < Lambda's 120 s
            // Validation rule: sampling duration must be >= 2 × attempt timeout.
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
        });

        return services;
    }
}
