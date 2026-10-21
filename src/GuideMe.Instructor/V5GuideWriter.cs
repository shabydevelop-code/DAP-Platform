using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Text.Json;

namespace GuideMe.Instructor;

// Explicitly scoped writer: creates only a Guides row; no schema changes or step data.
internal static class V5GuideWriter
{
    internal static V5GuideReader.GuideInfo RenameGuide(long id, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.Length > 256)
            throw new ArgumentException("שם הלומדה חייב להכיל בין 1 ל־256 תווים.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 3;
        command.CommandText = "UPDATE Guides SET GuideName=$name WHERE GuideId=$id RETURNING GuideKey";
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$id", id);
        var key = command.ExecuteScalar() as string;
        if (key is null)
            throw new InvalidOperationException("הלומדה לא נמצאה.");
        return new V5GuideReader.GuideInfo(id, key, name);
    }

    internal static V5GuideReader.GuideInfo CreateGuide(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("יש להזין שם לומדה.");
        if (name.Length > 256)
            throw new ArgumentException("שם הלומדה ארוך מדי.");
        if (!File.Exists(V5GuideReader.DatabasePath))
            throw new FileNotFoundException("מסד הנתונים V5 לא נמצא.", V5GuideReader.DatabasePath);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 3
        };
        var key = "guide-" + Guid.NewGuid().ToString("N");
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 3;
        command.CommandText = @"INSERT INTO Guides (GuideKey, GuideName)
VALUES ($key, $name) RETURNING GuideId";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$name", name);
        try
        {
            var id = Convert.ToInt64(command.ExecuteScalar());
            return new V5GuideReader.GuideInfo(id, key, name);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("מפתח הלומדה כבר קיים או שהנתונים אינם תקינים.", ex);
        }
    }

    internal static V5GuideReader.GuideInfo CreateGuideWithContext(
        string name, string contextId, string runtime, string matcherProperty, string matcherValue)
    {
        name = name.Trim();
        contextId = contextId.Trim();
        matcherValue = matcherValue.Trim();
        if (name.Length is < 1 or > 256)
            throw new ArgumentException("שם הלומדה חייב להכיל בין 1 ל־256 תווים.");
        if (string.IsNullOrWhiteSpace(contextId) || string.IsNullOrWhiteSpace(matcherValue))
            throw new ArgumentException("יש לבחור חלון יעד תקין לפני יצירת הלומדה.");
        if (runtime is not ("Web" or "Windows") ||
            (runtime == "Web" && matcherProperty != "UrlHost") ||
            (runtime == "Windows" && matcherProperty != "ProcessName"))
            throw new ArgumentException("מאפייני זיהוי האפליקציה אינם תקינים.");
        if (!File.Exists(V5GuideReader.DatabasePath))
            throw new FileNotFoundException("מסד הנתונים V5 לא נמצא.", V5GuideReader.DatabasePath);

        var key = "guide-" + Guid.NewGuid().ToString("N");
        if (matcherProperty == "UrlHost" && Uri.TryCreate(matcherValue, UriKind.Absolute, out var parsed))
            matcherValue = parsed.Authority;
        var matchers = JsonSerializer.Serialize(new[] { new { Order = 0, Kind = matcherProperty, Value = matcherValue } });
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var insertGuide = connection.CreateCommand();
        insertGuide.Transaction = transaction;
        insertGuide.CommandTimeout = 3;
        insertGuide.CommandText = "INSERT INTO Guides (GuideKey, GuideName) VALUES ($key, $name) RETURNING GuideId";
        insertGuide.Parameters.AddWithValue("$key", key);
        insertGuide.Parameters.AddWithValue("$name", name);
        var id = Convert.ToInt64(insertGuide.ExecuteScalar());

        using var insertContext = connection.CreateCommand();
        insertContext.Transaction = transaction;
        insertContext.CommandTimeout = 3;
        insertContext.CommandText = @"INSERT INTO ApplicationContexts (GuideId, ContextId, Runtime, MatchersJson)
VALUES ($guideId, $contextId, $runtime, $matchers)";
        insertContext.Parameters.AddWithValue("$guideId", id);
        insertContext.Parameters.AddWithValue("$contextId", contextId);
        insertContext.Parameters.AddWithValue("$runtime", runtime);
        insertContext.Parameters.AddWithValue("$matchers", matchers);
        if (insertContext.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("שמירת הקשר האפליקציה נכשלה.");
        transaction.Commit();
        return new V5GuideReader.GuideInfo(id, key, name);
    }

    // Contexts are independent guide entities; updates never overwrite steps.
    internal static void SaveApplicationContext(long guideId, string contextId, string runtime, string matcherProperty, string matcherValue)
    {
        if (runtime is not ("Web" or "Windows"))
            throw new ArgumentException("סוג אפליקציה לא נתמך.");
        if (string.IsNullOrWhiteSpace(contextId) || string.IsNullOrWhiteSpace(matcherValue))
            throw new ArgumentException("חסרים פרטי זיהוי אפליקציה.");
        var matchers = JsonSerializer.Serialize(new[] { new { Property = matcherProperty, Value = matcherValue } });
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 3;
        command.CommandText = @"INSERT INTO ApplicationContexts (GuideId, ContextId, Runtime, MatchersJson)
VALUES ($guideId, $contextId, $runtime, $matchers)
ON CONFLICT(GuideId, ContextId) DO UPDATE SET Runtime=excluded.Runtime, MatchersJson=excluded.MatchersJson";
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$contextId", contextId);
        command.Parameters.AddWithValue("$runtime", runtime);
        command.Parameters.AddWithValue("$matchers", matchers);
        command.ExecuteNonQuery();
    }
    internal static void DeleteGuide(long guideId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON";
            pragma.ExecuteNonQuery();
        }
        using var transaction = connection.BeginTransaction();
        // The V5 schema does not define ON DELETE CASCADE. Remove dependent
        // entities in foreign-key order, atomically, without touching other guides.
        foreach (var table in new[] {
            "StepRules", "StepTargets", "ElementAnchors", "Elements",
            "Steps", "ApplicationContexts", "Guides"
        })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 3;
            command.CommandText = "DELETE FROM " + table + " WHERE GuideId=$id";
            command.Parameters.AddWithValue("$id", guideId);
            var deleted = command.ExecuteNonQuery();
            if (table == "Guides" && deleted != 1)
                throw new InvalidOperationException("הלומדה לא נמצאה או כבר נמחקה.");
        }
        transaction.Commit();
    }

}
