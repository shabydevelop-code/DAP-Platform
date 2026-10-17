using Microsoft.Data.Sqlite;

namespace GuideMe.Data.Sqlite.Guides;

/// <summary>
/// Loads reusable V5 element definitions and step usages for the production learner.
/// </summary>
public static class GuideV5ElementReader
{
    public sealed record LocatorDefinition(string Strategy, string Value);
    public sealed record AnchorDefinition(string AnchorElementId, string Relation, int Order);
    public sealed record ElementDefinition(
        string ElementId, string? Runtime, string? FrameContextJson,
        IReadOnlyList<LocatorDefinition> Locators,
        IReadOnlyList<AnchorDefinition> Anchors);
    public sealed record ElementUsage(string StepId, string UsageId, string ElementId, string Purpose);
    public sealed record ElementCatalog(
        long GuideId, IReadOnlyDictionary<string, ElementDefinition> Elements,
        IReadOnlyList<ElementUsage> Usages);

    public static async Task<ElementCatalog> ReadAsync(
        string databasePath, string guideKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(guideKey);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);

        long guideId;
        await using (var guide = connection.CreateCommand())
        {
            guide.CommandText = "SELECT GuideId FROM Guides WHERE GuideKey=$guideKey";
            guide.Parameters.AddWithValue("$guideKey", guideKey);
            var id = await guide.ExecuteScalarAsync(cancellationToken);
            if (id is null || id is DBNull)
                throw new InvalidOperationException($"V5 guide '{guideKey}' was not found.");
            guideId = Convert.ToInt64(id);
        }

        var elements = new Dictionary<string, ElementDefinition>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT ElementId,Runtime,FrameContextJson
                FROM GuideElements WHERE GuideId=$guideId ORDER BY ElementId;
                """;
            command.Parameters.AddWithValue("$guideId", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                if (!elements.TryAdd(id, new ElementDefinition(
                    id, reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    Array.Empty<LocatorDefinition>(),
                    Array.Empty<AnchorDefinition>())))
                    throw new InvalidOperationException($"Duplicate V5 element '{id}'.");
            }
        }

        var locators = new Dictionary<string, List<LocatorDefinition>>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT ElementId,Strategy,Value
                FROM ElementLocators WHERE GuideId=$guideId ORDER BY ElementId,LocatorId;
                """;
            command.Parameters.AddWithValue("$guideId", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                if (!elements.ContainsKey(id))
                    throw new InvalidOperationException($"V5 locator refers to missing element '{id}'.");
                if (!locators.TryGetValue(id, out var list))
                    locators[id] = list = new List<LocatorDefinition>();
                list.Add(new LocatorDefinition(reader.GetString(1), reader.GetString(2)));
            }
        }

        var anchors = new Dictionary<string, List<AnchorDefinition>>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT ElementId,AnchorElementId,Relation,AnchorOrder
                FROM ElementAnchorRelations WHERE GuideId=$guideId
                ORDER BY ElementId,AnchorOrder;
                """;
            command.Parameters.AddWithValue("$guideId", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(0);
                var anchorId = reader.GetString(1);
                if (!elements.ContainsKey(id) || !elements.ContainsKey(anchorId))
                    throw new InvalidOperationException($"Invalid V5 anchor '{id}' -> '{anchorId}'.");
                if (!anchors.TryGetValue(id, out var list))
                    anchors[id] = list = new List<AnchorDefinition>();
                list.Add(new AnchorDefinition(anchorId,
                    reader.GetString(2), reader.GetInt32(3)));
            }
        }

        foreach (var (id, element) in elements.ToArray())
        {
            if (!locators.TryGetValue(id, out var elementLocators) || elementLocators.Count == 0)
                throw new InvalidOperationException($"V5 element '{id}' has no locator.");
            elements[id] = element with
            {
                Locators = elementLocators,
                Anchors = anchors.TryGetValue(id, out var elementAnchors)
                    ? elementAnchors : Array.Empty<AnchorDefinition>()
            };
        }

        var usages = new List<ElementUsage>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT StepId,UsageId,ElementId,Purpose
                FROM StepElementUsages WHERE GuideId=$guideId
                ORDER BY StepId,UsageId;
                """;
            command.Parameters.AddWithValue("$guideId", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetString(2);
                if (!elements.ContainsKey(id))
                    throw new InvalidOperationException($"V5 usage refers to missing element '{id}'.");
                usages.Add(new ElementUsage(reader.GetString(0), reader.GetString(1),
                    id, reader.GetString(3)));
            }
        }

        return new ElementCatalog(guideId, elements, usages);
    }
}
