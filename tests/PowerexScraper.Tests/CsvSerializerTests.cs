using PowerexScraper.Csv;
using PowerexScraper.Flattening;

namespace PowerexScraper.Tests;

public class CsvSerializerTests
{
    private static Row MakeRow(
        Dictionary<string, string> dims,
        string timestamp,
        Dictionary<string, string> values,
        Dictionary<string, string>? enrichment = null)
        => new(dims, new Dictionary<string, string>(), DateTimeOffset.Parse(timestamp), "PT60M", values)
           { Enrichment = enrichment ?? new Dictionary<string, string>() };

    [Fact]
    public void Header_and_row_follow_the_fixed_column_order()
    {
        var result = new FlattenResult(
            ["AREA"],
            ["GENERATION_FORECAST", "ACTUAL_GENERATION", "SCHEDULED_CONSUMPTION"],
            [],
            [MakeRow(new() { ["AREA"] = "CTA|10YSK-SEPS-----K" }, "2026-08-30T22:00:00Z",
                     new() { ["GENERATION_FORECAST"] = "", ["ACTUAL_GENERATION"] = "2665.90",
                             ["SCHEDULED_CONSUMPTION"] = "" })]);

        var lines = CsvSerializer.Serialize(result).Split("\r\n");
        Assert.Equal("AREA,timestamp_utc,resolution,GENERATION_FORECAST,ACTUAL_GENERATION,SCHEDULED_CONSUMPTION",
                     lines[0]);
        Assert.Equal("CTA|10YSK-SEPS-----K,2026-08-30T22:00:00Z,PT60M,,2665.90,", lines[1]);
    }

    [Fact]
    public void Enrichment_columns_come_last_in_config_order()
    {
        var result = new FlattenResult(
            ["AREA", "GENERATION_UNIT"],
            ["ACTUAL_GENERATION_OUTPUT"],
            ["unitName", "installedCapacity"],
            [MakeRow(new() { ["AREA"] = "CTA|X", ["GENERATION_UNIT"] = "U1" }, "2026-08-29T22:00:00Z",
                     new() { ["ACTUAL_GENERATION_OUTPUT"] = "243.49" },
                     new() { ["unitName"] = "Bohunice TG31", ["installedCapacity"] = "250" })]);

        var lines = CsvSerializer.Serialize(result).Split("\r\n");
        Assert.Equal("AREA,GENERATION_UNIT,timestamp_utc,resolution,ACTUAL_GENERATION_OUTPUT,unitName,installedCapacity",
                     lines[0]);
        Assert.Equal("CTA|X,U1,2026-08-29T22:00:00Z,PT60M,243.49,Bohunice TG31,250", lines[1]);
    }

    [Fact]
    public void Values_with_commas_quotes_and_newlines_are_rfc4180_escaped()
    {
        var result = new FlattenResult(
            ["AREA"], ["V"], [],
            [MakeRow(new() { ["AREA"] = "has,comma" }, "2026-01-01T00:00:00Z",
                     new() { ["V"] = "say \"hi\"\r\nnewline" })]);

        var csv = CsvSerializer.Serialize(result);
        Assert.Contains("\"has,comma\"", csv);
        Assert.Contains("\"say \"\"hi\"\"\r\nnewline\"", csv);
    }

    [Fact]
    public void Missing_row_value_for_a_column_serializes_as_empty()
    {
        var result = new FlattenResult(
            ["AREA"], ["A", "B"], [],
            [MakeRow(new() { ["AREA"] = "X" }, "2026-01-01T00:00:00Z", new() { ["A"] = "1.00" })]);
        var lines = CsvSerializer.Serialize(result).Split("\r\n");
        Assert.Equal("X,2026-01-01T00:00:00Z,PT60M,1.00,", lines[1]);
    }
}
