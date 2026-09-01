using System.Text.Json;

namespace PowerexScraper.Entsoe;

public static class EnvelopeValidator
{
    private const int ExcerptLength = 2000;

    public static void Validate(JsonDocument doc)
    {
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new EntsoeContractException(
                $"Response root is not a JSON object (got {root.ValueKind}).", Excerpt(root));

        if (root.TryGetProperty("uuAppErrorMap", out var errorMap)
            && errorMap.ValueKind == JsonValueKind.Object)
        {
            using var errors = errorMap.EnumerateObject();
            if (errors.MoveNext())
                throw new EntsoeContractException(
                    $"API returned errors in uuAppErrorMap (first: {errors.Current.Name})",
                    Excerpt(root));
        }

        if (!root.TryGetProperty("instanceList", out var instances) || instances.ValueKind != JsonValueKind.Array)
            throw new EntsoeContractException("Response has no 'instanceList' array.", Excerpt(root));

        if (!root.TryGetProperty("metaData", out var metaData) || metaData.ValueKind != JsonValueKind.Array)
            throw new EntsoeContractException("Response has no 'metaData' array.", Excerpt(root));
    }

    private static string Excerpt(JsonElement root)
    {
        var raw = root.GetRawText();
        return raw.Length <= ExcerptLength ? raw : raw[..ExcerptLength];
    }
}
