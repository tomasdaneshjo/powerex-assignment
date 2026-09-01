using PowerexScraper.Config;

namespace PowerexScraper.Tests;

public class ConfigTests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void Load_binds_all_datasets_from_packaged_endpoints_json()
    {
        var config = AppConfig.Load(NoEnv);

        Assert.Equal(new Uri("https://iop-transparency.entsoe.eu"), config.BaseUrl);
        Assert.Equal(3, config.Datasets.Count);

        var forecast = config.Datasets[0];
        Assert.Equal("generation-forecast-dayahead", forecast.Id);
        Assert.Equal("generation/forecast/dayAhead", forecast.RoutePath);
        Assert.Equal(new[] { "CTA|10YSK-SEPS-----K" }, forecast.Areas);
        Assert.Equal("CET", forecast.TimeZone);
        Assert.Equal(new WindowSpec("Europe/Bratislava", 1, 1), forecast.Window);
        Assert.Null(forecast.Enrichment);

        var perUnit = config.Datasets[1];
        Assert.Equal("generation-actual-perunit", perUnit.Id);
        Assert.Equal(new WindowSpec("Europe/Bratislava", -1, 1), perUnit.Window);
        Assert.NotNull(perUnit.Enrichment);
        Assert.Equal("supplement", perUnit.Enrichment!.Source);
        Assert.Equal("COMPOSITE_EIC", perUnit.Enrichment.InstanceKeyAttribute);
        Assert.Equal(new[] { "unitName", "productionType", "installedCapacity" }, perUnit.Enrichment.Columns);

        var perUnitCz = config.Datasets[2];
        Assert.Equal("generation-actual-perunit-cz", perUnitCz.Id);
        Assert.Equal("generation/actual/perUnit", perUnitCz.RoutePath);
        Assert.Equal(new[] { "CTA|10YCZ-CEPS-----N" }, perUnitCz.Areas);
        Assert.Equal(new[] { "unitName", "locationName", "voltageLevel", "productionUnitEIC" },
                     perUnitCz.Enrichment!.Columns);
    }

    [Fact]
    public void Load_env_vars_override_base_url_and_set_bucket()
    {
        var env = new Dictionary<string, string?>
        {
            ["ENTSOE_BASE_URL"] = "https://override.entsoe.test",
            ["OUTPUT_BUCKET"] = "my-bucket",
        };
        var config = AppConfig.Load(k => env.GetValueOrDefault(k));

        Assert.Equal(new Uri("https://override.entsoe.test"), config.BaseUrl);
        Assert.Equal("my-bucket", config.OutputBucket);
    }

    [Fact]
    public void Load_rejects_duplicate_dataset_ids()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, """
        { "datasets": [
          { "id": "a", "routePath": "x/y", "areas": ["CTA|E1"], "timeZone": "CET",
            "window": { "anchorTimeZone": "Europe/Bratislava", "startOffsetDays": 0, "durationDays": 1 } },
          { "id": "a", "routePath": "x/z", "areas": ["CTA|E1"], "timeZone": "CET",
            "window": { "anchorTimeZone": "Europe/Bratislava", "startOffsetDays": 0, "durationDays": 1 } }
        ] }
        """);
        Assert.Throws<InvalidOperationException>(() => AppConfig.Load(NoEnv, path));
    }

    [Fact]
    public void Load_rejects_a_dataset_missing_a_required_field()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, """
        { "datasets": [
          { "id": "a", "areas": ["CTA|E1"], "timeZone": "CET",
            "window": { "anchorTimeZone": "Europe/Bratislava", "startOffsetDays": 0, "durationDays": 1 } }
        ] }
        """); // routePath omitted
        var ex = Assert.Throws<InvalidOperationException>(() => AppConfig.Load(NoEnv, path));
        Assert.Contains("'a'", ex.Message);
        Assert.Contains("routePath", ex.Message);
    }
}
