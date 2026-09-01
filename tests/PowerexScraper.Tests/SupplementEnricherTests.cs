using System.Text.Json;
using PowerexScraper.Config;
using PowerexScraper.Flattening;

namespace PowerexScraper.Tests;

public class SupplementEnricherTests
{
    private static readonly EnrichmentSpec Spec =
        new("supplement", "COMPOSITE_EIC", ["unitName", "productionType", "installedCapacity"]);

    [Fact]
    public void PerUnit_rows_gain_unit_master_data_from_supplement()
    {
        using var doc = CurveFlattenerTests.LoadFixture("prod-perunit-seps.json");
        var enriched = SupplementEnricher.Enrich(CurveFlattener.Flatten(doc), doc, Spec);

        Assert.Equal(new[] { "unitName", "productionType", "installedCapacity" }, enriched.EnrichmentColumns);
        var bohunice = enriched.Rows.First(r => r.Dimensions["GENERATION_UNIT"] == "24WG--EBOG31---D");
        Assert.Equal("Bohunice TG31", bohunice.Enrichment["unitName"]);
        Assert.Equal("B14", bohunice.Enrichment["productionType"]);
        Assert.Equal("250", bohunice.Enrichment["installedCapacity"]);
    }

    [Fact]
    public void Missing_supplement_entry_yields_empty_strings()
    {
        using var doc = JsonDocument.Parse("""
        {
          "uuAppErrorMap": {},
          "metaData": [ { "code": "V", "name": "v" } ],
          "instanceList": [ {
            "businessDimensionMap": { "AREA": "CTA|X" },
            "instanceAttributeMap": { "COMPOSITE_EIC": "NOT-IN-SUPPLEMENT:0" },
            "curveData": { "periodList": [ {
              "timeInterval": { "from": "2026-01-01T23:00:00Z", "to": "2026-01-02T23:00:00Z" },
              "resolution": "PT60M",
              "pointMap": { "0": ["1.00"] }
            } ] }
          } ],
          "supplement": {}
        }
        """);
        var enriched = SupplementEnricher.Enrich(CurveFlattener.Flatten(doc), doc, Spec);
        Assert.Equal("", enriched.Rows[0].Enrichment["unitName"]);
    }

    [Fact]
    public void Missing_supplement_object_entirely_yields_empty_strings()
    {
        using var doc = CurveFlattenerTests.LoadFixture("prod-dayahead-seps.json"); // no supplement key
        var enriched = SupplementEnricher.Enrich(CurveFlattener.Flatten(doc), doc, Spec);
        Assert.All(enriched.Rows, r => Assert.Equal("", r.Enrichment["unitName"]));
    }
}
