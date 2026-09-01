using System.Text.Json;
using PowerexScraper.Flattening;

namespace PowerexScraper.Tests;

public class CurveFlattenerTests
{
    internal static JsonDocument LoadFixture(string name)
        => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    [Fact]
    public void DayAhead_fixture_flattens_to_96_pt15m_rows()
    {
        using var doc = LoadFixture("prod-dayahead-seps.json");
        var result = CurveFlattener.Flatten(doc);

        Assert.Equal(new[] { "AREA" }, result.DimensionColumns);
        Assert.Equal(new[] { "GENERATION_FORECAST", "ACTUAL_GENERATION", "SCHEDULED_CONSUMPTION" },
                     result.ValueColumns);
        Assert.Empty(result.EnrichmentColumns);
        Assert.Equal(96, result.Rows.Count);

        var first = result.Rows[0];
        Assert.Equal("CTA|10YSK-SEPS-----K", first.Dimensions["AREA"]);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 22, 0, 0, TimeSpan.Zero), first.TimestampUtc);
        Assert.Equal("PT15M", first.Resolution);
        Assert.Equal("", first.Values["GENERATION_FORECAST"]);        // {"alt":"N/A"}
        Assert.Equal("2665.90", first.Values["ACTUAL_GENERATION"]);   // verbatim string

        // timestamps advance by the declared resolution, not by assumption
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 22, 15, 0, TimeSpan.Zero), result.Rows[1].TimestampUtc);
    }

    [Fact]
    public void PerUnit_fixture_flattens_23_units_x_24_hours()
    {
        using var doc = LoadFixture("prod-perunit-seps.json");
        var result = CurveFlattener.Flatten(doc);

        // alphabetical union of businessDimensionMap keys
        Assert.Equal(new[] { "AREA", "GENERATION_UNIT", "PRODUCTION_TYPE" }, result.DimensionColumns);
        Assert.Equal(new[] { "ACTUAL_GENERATION_OUTPUT", "ACTUAL_CONSUMPTION" }, result.ValueColumns);
        Assert.Equal(23 * 24, result.Rows.Count);

        var first = result.Rows[0];
        Assert.Equal("24WG--EBOG31---D", first.Dimensions["GENERATION_UNIT"]);
        Assert.Equal("B14", first.Dimensions["PRODUCTION_TYPE"]);
        Assert.Equal("PT60M", first.Resolution);
        Assert.Equal("243.49", first.Values["ACTUAL_GENERATION_OUTPUT"]);
        Assert.Equal("", first.Values["ACTUAL_CONSUMPTION"]);  // null cell
        Assert.Equal("24WG--EBOG31---D:0", first.InstanceAttributes["COMPOSITE_EIC"]);
    }

    [Fact]
    public void Iop_fixture_with_all_alt_cells_produces_empty_values_without_throwing()
    {
        using var doc = LoadFixture("iop-perunit-seps-all-na.json");
        var result = CurveFlattener.Flatten(doc);
        Assert.All(result.Rows, r => Assert.All(r.Values.Values, v => Assert.Equal("", v)));
    }

    [Fact]
    public void Unknown_extra_metadata_column_becomes_a_new_value_column()
    {
        using var doc = JsonDocument.Parse("""
        {
          "uuAppErrorMap": {},
          "metaData": [ { "code": "KNOWN", "name": "k" }, { "code": "BRAND_NEW", "name": "n" } ],
          "instanceList": [ {
            "businessDimensionMap": { "AREA": "CTA|X" },
            "instanceAttributeMap": {},
            "curveData": { "periodList": [ {
              "timeInterval": { "from": "2026-01-01T23:00:00Z", "to": "2026-01-02T23:00:00Z" },
              "resolution": "PT60M",
              "pointMap": { "0": ["1.00", "2.00"] }
            } ] }
          } ]
        }
        """);
        var result = CurveFlattener.Flatten(doc);
        Assert.Equal(new[] { "KNOWN", "BRAND_NEW" }, result.ValueColumns);
        Assert.Equal("2.00", result.Rows[0].Values["BRAND_NEW"]);
    }

    [Fact]
    public void Unknown_extra_dimension_becomes_a_new_dimension_column()
    {
        using var doc = JsonDocument.Parse("""
        {
          "uuAppErrorMap": {},
          "metaData": [ { "code": "V", "name": "v" } ],
          "instanceList": [ {
            "businessDimensionMap": { "AREA": "CTA|X", "ZBRAND_NEW_DIM": "hello" },
            "instanceAttributeMap": {},
            "curveData": { "periodList": [ {
              "timeInterval": { "from": "2026-01-01T23:00:00Z", "to": "2026-01-02T23:00:00Z" },
              "resolution": "PT60M",
              "pointMap": { "0": ["1.00"] }
            } ] }
          } ]
        }
        """);
        var result = CurveFlattener.Flatten(doc);
        Assert.Equal(new[] { "AREA", "ZBRAND_NEW_DIM" }, result.DimensionColumns);
        Assert.Equal("hello", result.Rows[0].Dimensions["ZBRAND_NEW_DIM"]);
    }

    [Fact]
    public void Empty_period_list_yields_zero_rows_without_throwing()
    {
        using var doc = JsonDocument.Parse("""
        {
          "uuAppErrorMap": {},
          "metaData": [ { "code": "V", "name": "v" } ],
          "instanceList": [ {
            "businessDimensionMap": { "AREA": "CTA|X" },
            "instanceAttributeMap": {},
            "curveData": { "periodList": [] }
          } ]
        }
        """);
        Assert.Empty(CurveFlattener.Flatten(doc).Rows);
    }

    [Fact]
    public void Point_array_shorter_than_metadata_leaves_missing_columns_empty()
    {
        using var doc = JsonDocument.Parse("""
        {
          "uuAppErrorMap": {},
          "metaData": [ { "code": "A", "name": "a" }, { "code": "B", "name": "b" } ],
          "instanceList": [ {
            "businessDimensionMap": { "AREA": "CTA|X" },
            "instanceAttributeMap": {},
            "curveData": { "periodList": [ {
              "timeInterval": { "from": "2026-01-01T23:00:00Z", "to": "2026-01-02T23:00:00Z" },
              "resolution": "PT60M",
              "pointMap": { "0": ["1.00"] }
            } ] }
          } ]
        }
        """);
        var row = CurveFlattener.Flatten(doc).Rows[0];
        Assert.Equal("1.00", row.Values["A"]);
        Assert.Equal("", row.Values["B"]);
    }
}
