using DAP.Core.Targets;

namespace DAP.Core.Guides;

/// <summary>
/// Persisted definition of an application that can host Guide Steps.
/// Concrete browser-tab/window identities are runtime session state and are not persisted here.
/// </summary>
public sealed record GuideApplicationContext(
    string Key,
    TargetRuntime Runtime,
    IReadOnlyList<ApplicationContextMatcher> Matchers);

public sealed record ApplicationContextMatcher(
    int Order,
    string Kind,
    string Value);
