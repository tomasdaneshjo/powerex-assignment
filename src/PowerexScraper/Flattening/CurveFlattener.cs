using System.Text.Json;
using System.Xml;

namespace PowerexScraper.Flattening;

/// <summary>Generic walker over the platform's universal envelope (spec §3):
/// instanceList → curveData.periodList → pointMap. Column meaning comes from metaData
/// at runtime — this class contains zero endpoint-specific knowledge.</summary>
public static class CurveFlattener
{
    public static FlattenResult Flatten(JsonDocument doc)
    {
        var root = doc.RootElement;

        var valueColumns = root.GetProperty("metaData").EnumerateArray()
            .Select(m => m.GetProperty("code").GetString()!)
            .ToList();

        var dimensionColumns = new SortedSet<string>(StringComparer.Ordinal);
        var rows = new List<Row>();

        foreach (var instance in root.GetProperty("instanceList").EnumerateArray())
        {
            var dimensions = ReadStringMap(instance, "businessDimensionMap");
            var attributes = ReadStringMap(instance, "instanceAttributeMap");
            foreach (var key in dimensions.Keys)
                dimensionColumns.Add(key);

            if (!instance.TryGetProperty("curveData", out var curveData)
                || !curveData.TryGetProperty("periodList", out var periods)
                || periods.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var period in periods.EnumerateArray())
            {
                var from = period.GetProperty("timeInterval").GetProperty("from").GetDateTimeOffset();
                var resolutionText = period.GetProperty("resolution").GetString()!;
                var step = XmlConvert.ToTimeSpan(resolutionText);

                foreach (var point in period.GetProperty("pointMap").EnumerateObject())
                {
                    var index = int.Parse(point.Name, System.Globalization.CultureInfo.InvariantCulture);
                    var values = new Dictionary<string, string>(valueColumns.Count);
                    for (var i = 0; i < valueColumns.Count; i++)
                    {
                        values[valueColumns[i]] = i < point.Value.GetArrayLength()
                            ? CellDecoder.Decode(point.Value[i])
                            : "";
                    }
                    rows.Add(new Row(dimensions, attributes, from + index * step, resolutionText, values));
                }
            }
        }

        // pointMap is a JSON object — key order is not guaranteed; sort rows for determinism.
        var ordered = rows
            .OrderBy(r => string.Join("", r.Dimensions.OrderBy(d => d.Key, StringComparer.Ordinal)
                                                            .Select(d => d.Value)),
                     StringComparer.Ordinal)
            .ThenBy(r => r.TimestampUtc)
            .ToList();

        return new FlattenResult(dimensionColumns.ToList(), valueColumns, [], ordered);
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement parent, string propertyName)
    {
        var map = new Dictionary<string, string>();
        if (parent.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.Object)
            foreach (var p in element.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    map[p.Name] = p.Value.GetString()!;
        return map;
    }
}
