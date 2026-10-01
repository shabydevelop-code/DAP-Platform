namespace DAP.Core.Guides;

public sealed record ValidationDefinition(
    string Kind,
    string? ExpectedValue = null,
    IReadOnlyDictionary<string, string>? Options = null);
