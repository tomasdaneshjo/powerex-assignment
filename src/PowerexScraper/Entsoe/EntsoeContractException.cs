namespace PowerexScraper.Entsoe;

/// <summary>The response no longer matches the contract captured on 2026-08-31 (spec §3) —
/// a named, diagnosable failure instead of a NullReferenceException deep in the flattener.</summary>
public sealed class EntsoeContractException(string message, string? bodyExcerpt = null) : Exception(message)
{
    public string? BodyExcerpt { get; } = bodyExcerpt;
}
