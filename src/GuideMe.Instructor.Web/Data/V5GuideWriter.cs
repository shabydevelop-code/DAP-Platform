using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Text.Json;

namespace GuideMe.Instructor.Web.Data;

// Explicitly scoped writer: creates only a Guides row; no schema changes or step data.
internal static class V5GuideWriter
{
    // A bare host:port (e.g. localhost:5200) must not be interpreted as a URI scheme.
    private static string NormalizeWebAuthority(string value)
    {
        value = value.Trim();
        var url = value.Contains("://", StringComparison.Ordinal) ? value : "http://" + value;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            Uri.CheckHostName(uri.Host) == UriHostNameType.Unknown)
            throw new ArgumentException("כתובת אפליקציית Web אינה תקינה.");
        return uri.Authority;
    }

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
            (runtime == "Windows" && matcherProperty is not ("ProcessName" or "AutomationId")))
            throw new ArgumentException("מאפייני זיהוי האפליקציה אינם תקינים.");
        if (!File.Exists(V5GuideReader.DatabasePath))
            throw new FileNotFoundException("מסד הנתונים V5 לא נמצא.", V5GuideReader.DatabasePath);

        var key = "guide-" + Guid.NewGuid().ToString("N");
        if (matcherProperty == "UrlHost")
            matcherValue = NormalizeWebAuthority(matcherValue);
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

    internal sealed record SelectedApplication(string ContextId, string Runtime,
        string MatcherProperty, string MatcherValue);

    internal static V5GuideReader.GuideInfo CreateGuideWithApplications(
        string name, IReadOnlyList<SelectedApplication> applications)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 256)
            throw new ArgumentException("שם הלומדה חייב להכיל בין 1 ל־256 תווים.");
        if (applications.Count is < 1 or > 30)
            throw new ArgumentException("יש לבחור בין אפליקציה אחת ל־30 אפליקציות.");
        if (applications.Select(a => a.ContextId).Distinct(StringComparer.Ordinal).Count() != applications.Count)
            throw new ArgumentException("נמצאו מזהי אפליקציות כפולים.");

        var normalized = new List<(string Id, string Runtime, string Matchers)>();
        foreach (var app in applications)
        {
            if (string.IsNullOrWhiteSpace(app.ContextId) || app.ContextId.Length > 100 ||
                app.ContextId != app.ContextId.Trim())
                throw new ArgumentException("מזהה אפליקציה לא תקין.");
            var value = app.MatcherValue.Trim();
            if (value.Length is < 1 or > 4096 ||
                (app.Runtime == "Web" && app.MatcherProperty != "UrlHost") ||
                (app.Runtime == "Windows" && app.MatcherProperty != "ProcessName") ||
                (app.Runtime is not ("Web" or "Windows")))
                throw new ArgumentException("מאפייני זיהוי האפליקציה אינם תקינים.");
            if (app.Runtime == "Web") value = NormalizeWebAuthority(value);
            normalized.Add((app.ContextId, app.Runtime, JsonSerializer.Serialize(
                new[] { new { Order = 0, Kind = app.MatcherProperty, Value = value } })));
        }

        var key = "guide-" + Guid.NewGuid().ToString("N");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var guide = connection.CreateCommand();
        guide.Transaction = transaction;
        guide.CommandTimeout = 3;
        guide.CommandText = "INSERT INTO Guides (GuideKey, GuideName) VALUES ($key, $name) RETURNING GuideId";
        guide.Parameters.AddWithValue("$key", key);
        guide.Parameters.AddWithValue("$name", name);
        var guideId = Convert.ToInt64(guide.ExecuteScalar());
        foreach (var context in normalized)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 3;
            command.CommandText = @"INSERT INTO ApplicationContexts
(GuideId, ContextId, Runtime, MatchersJson)
VALUES ($guideId, $contextId, $runtime, $matchers)";
            command.Parameters.AddWithValue("$guideId", guideId);
            command.Parameters.AddWithValue("$contextId", context.Id);
            command.Parameters.AddWithValue("$runtime", context.Runtime);
            command.Parameters.AddWithValue("$matchers", context.Matchers);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        return new V5GuideReader.GuideInfo(guideId, key, name);
    }

    internal static void AssignStepApplication(long guideId, string stepId, string contextId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandTimeout = 3;
        check.CommandText = "SELECT Runtime FROM ApplicationContexts WHERE GuideId=$id AND ContextId=$context";
        check.Parameters.AddWithValue("$id", guideId);
        check.Parameters.AddWithValue("$context", contextId);
        var runtime = check.ExecuteScalar() as string;
        if (runtime is null) throw new InvalidOperationException("האפליקציה אינה שייכת ללומדה.");
        using var step = connection.CreateCommand();
        step.Transaction = transaction;
        step.CommandTimeout = 3;
        step.CommandText = @"UPDATE Steps SET ApplicationContextId=$context, Runtime=$runtime
WHERE GuideId=$id AND StepId=$stepId";
        step.Parameters.AddWithValue("$context", contextId);
        step.Parameters.AddWithValue("$runtime", runtime);
        step.Parameters.AddWithValue("$id", guideId);
        step.Parameters.AddWithValue("$stepId", stepId);
        if (step.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("השלב אינו קיים.");
        transaction.Commit();
    }

    // Contexts are independent guide entities; updates never overwrite steps.
    internal static void SaveApplicationContext(long guideId, string contextId, string runtime, string matcherProperty, string matcherValue)
    {
        if (runtime is not ("Web" or "Windows"))
            throw new ArgumentException("סוג אפליקציה לא נתמך.");
        if (string.IsNullOrWhiteSpace(contextId) || string.IsNullOrWhiteSpace(matcherValue))
            throw new ArgumentException("חסרים פרטי זיהוי אפליקציה.");
        contextId = contextId.Trim();
        matcherValue = matcherValue.Trim();
        if ((runtime == "Web" && matcherProperty != "UrlHost") ||
            (runtime == "Windows" && matcherProperty != "ProcessName"))
            throw new ArgumentException("מאפייני זיהוי האפליקציה אינם תקינים.");
        if (runtime == "Web")
            matcherValue = NormalizeWebAuthority(matcherValue);
        var matchers = JsonSerializer.Serialize(new[] { new { Order = 0, Kind = matcherProperty, Value = matcherValue } });
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
    internal static void RenameApplication(long guideId, string contextId, string displayName)
    {
        displayName = displayName.Trim();
        if (displayName.Length is < 1 or > 100)
            throw new ArgumentException("שם האפליקציה חייב להכיל בין 1 ל־100 תווים.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandTimeout = 3;
            schema.CommandText = "PRAGMA table_info(ApplicationContexts)";
            using var reader = schema.ExecuteReader();
            var found = false;
            while (reader.Read())
                if (reader.GetString(1) == "DisplayName") found = true;
            reader.Close();
            if (!found)
            {
                schema.CommandText = "ALTER TABLE ApplicationContexts ADD COLUMN DisplayName TEXT";
                schema.ExecuteNonQuery();
            }
        }
        using var command = connection.CreateCommand();
        command.CommandTimeout = 3;
        command.CommandText = @"UPDATE ApplicationContexts SET DisplayName=$name
WHERE GuideId=$guideId AND ContextId=$contextId";
        command.Parameters.AddWithValue("$name", displayName);
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$contextId", contextId);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("האפליקציה אינה קיימת.");
    }

    internal static int CountApplicationSteps(long guideId, string contextId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadOnly,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 3;
        command.CommandText = @"SELECT COUNT(*) FROM Steps
WHERE GuideId=$guideId AND ApplicationContextId=$contextId";
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$contextId", contextId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    internal static void DeleteApplication(long guideId, string contextId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON";
            pragma.ExecuteNonQuery();
        }
        using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandTimeout = 3;
            check.CommandText = @"SELECT COUNT(*) FROM Steps
WHERE GuideId=$guideId AND ApplicationContextId=$contextId";
            check.Parameters.AddWithValue("$guideId", guideId);
            check.Parameters.AddWithValue("$contextId", contextId);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0)
                throw new InvalidOperationException("לא ניתן להסיר אפליקציה המשויכת לשלבים.");
        }
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 3;
        command.CommandText = @"DELETE FROM ApplicationContexts
WHERE GuideId=$guideId AND ContextId=$contextId";
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$contextId", contextId);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("האפליקציה אינה קיימת.");
        transaction.Commit();
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


    // Edit only fields already represented by the V5 Steps entity.
    // Never recreate a step or overwrite its targets, rules or captures.
    internal static void UpdateStep(long guideId, string stepId, string? bubble, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("מזהה השלב חסר.");
        if (bubble is { Length: > 4000 })
            throw new ArgumentException("טקסט הבועית ארוך מדי.");
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
        command.CommandText = @"UPDATE Steps
SET BubbleContent=$bubble, IsEnabled=$enabled
WHERE GuideId=$guideId AND StepId=$stepId";
        command.Parameters.AddWithValue("$bubble", (object?)bubble ?? DBNull.Value);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("השלב לא נמצא.");
    }
}
