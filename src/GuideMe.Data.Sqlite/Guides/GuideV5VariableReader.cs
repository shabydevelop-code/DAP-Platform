using GuideMe.Core.Guides;
using Microsoft.Data.Sqlite;

namespace GuideMe.Data.Sqlite.Guides;

/// <summary>Read canonical guide variable definitions; no legacy-capture fallback.</summary>
public static class GuideV5VariableReader
{
    public static async Task<IReadOnlyList<GuideVariableDefinition>> ReadDefinitionsAsync(
        string databasePath, long guideId, CancellationToken token = default)
    {
        await using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await db.OpenAsync(token);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT VariableId,DisplayName,InitialValue FROM GuideVariables WHERE GuideId=$guide ORDER BY rowid";
        cmd.Parameters.AddWithValue("$guide", guideId);
        await using var reader = await cmd.ExecuteReaderAsync(token);
        var definitions = new List<GuideVariableDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(token))
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !ids.Add(id))
                throw new InvalidOperationException("Invalid or duplicate guide variable.");
            definitions.Add(new GuideVariableDefinition(id, name,
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return definitions;
    }
}
