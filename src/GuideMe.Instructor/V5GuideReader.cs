using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace GuideMe.Instructor;

// Read-only V5 browser. It does not create, migrate, or write any database.
internal static class V5GuideReader
{
    internal const string DatabasePath = @"C:\ProgramData\GuideMe\Data\GuideMe-V5.db";
    internal sealed record GuideInfo(long Id, string Key, string Name);
    internal sealed record StepInfo(string Id, int Order, bool Enabled, string? Bubble);

    private static SqliteConnection Open()
    {
        if (!File.Exists(DatabasePath))
            throw new FileNotFoundException("מסד הנתונים V5 לא נמצא.", DatabasePath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 3
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    internal static List<GuideInfo> ListGuides()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT GuideId, GuideKey, GuideName FROM Guides ORDER BY GuideName";
        using var reader = command.ExecuteReader();
        var guides = new List<GuideInfo>();
        while (reader.Read())
            guides.Add(new GuideInfo(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        return guides;
    }

    internal static List<StepInfo> ListSteps(long guideId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT StepId, StepOrder, IsEnabled, BubbleContent
FROM Steps WHERE GuideId=$guideId ORDER BY StepOrder";
        command.Parameters.AddWithValue("$guideId", guideId);
        using var reader = command.ExecuteReader();
        var steps = new List<StepInfo>();
        while (reader.Read())
            steps.Add(new StepInfo(reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2) == 1,
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return steps;
    }

    // Explicit table allow-list from docs/schema/GuideMe-V5.sql; never interpolate user input.
    private static readonly HashSet<string> AllowedTables = new(StringComparer.Ordinal)
    {
        "Guides", "ApplicationContexts", "Steps", "Elements",
        "ElementAnchors", "StepTargets", "StepRules"
    };

    internal static string ReadTable(long guideId, string table, string? stepId = null)
    {
        if (!AllowedTables.Contains(table))
            throw new ArgumentException("טבלה לא מוכרת.", nameof(table));
        using var connection = Open();
        using var command = connection.CreateCommand();
        var stepFilter = stepId is not null && table is "Steps" or "StepTargets" or "StepRules";
        var elementFilter = stepId is not null && table is "Elements" or "ElementAnchors";
        var sql = $"SELECT * FROM {table} WHERE GuideId=$guideId";
        if (stepFilter)
            sql += " AND StepId=$stepId";
        else if (elementFilter)
        {
            const string used = @"SELECT u.ElementId FROM StepTargets u
                WHERE u.GuideId=$guideId AND u.StepId=$stepId";
            const string anchors = @"SELECT a.AnchorElementId FROM ElementAnchors a
                WHERE a.GuideId=$guideId AND a.ElementId IN (" + used + ")";
            sql += table switch
            {
                "Elements" => $" AND ElementId IN ({used} UNION {anchors})",
                "ElementAnchors" => $" AND ElementId IN ({used})",
                _ => throw new InvalidOperationException("Unexpected element table.")
            };
        }
        command.CommandText = sql;
        command.Parameters.AddWithValue("$guideId", guideId);
        if (stepFilter || elementFilter)
            command.Parameters.AddWithValue("$stepId", stepId!);
        using var reader = command.ExecuteReader();
        var output = new System.Text.StringBuilder();
        var count = 0;
        while (reader.Read())
        {
            count++;
            output.AppendLine($"[{count}]");
            for (var i = 0; i < reader.FieldCount; i++)
                output.AppendLine($"{reader.GetName(i)}: {(reader.IsDBNull(i) ? "(NULL)" : reader.GetValue(i))}");
            output.AppendLine();
        }
        if (count == 0)
            output.AppendLine("אין רשומות.");
        return output.ToString();
    }
}
