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

    // A target-free step is intentionally manual in the production V5 compiler.
    // Its application context is selected from this guide's saved contexts.
    internal static string CreateManualStep(long guideId, string contextId, string content,
        string placement)
    {
        content = content.Trim();
        if (content.Length is < 1 or > 4000)
            throw new ArgumentException("תוכן הבועית חייב להכיל בין 1 ל־4000 תווים.");
        if (placement != "Center")
            throw new ArgumentException("מיקום הבועית אינו תקין.");
        if (string.IsNullOrWhiteSpace(contextId))
            throw new ArgumentException("יש לבחור אפליקציה לשלב.");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandTimeout = 3;
            check.CommandText = @"SELECT COUNT(*) FROM ApplicationContexts
WHERE GuideId=$guideId AND ContextId=$contextId";
            check.Parameters.AddWithValue("$guideId", guideId);
            check.Parameters.AddWithValue("$contextId", contextId);
            if (Convert.ToInt64(check.ExecuteScalar()) != 1)
                throw new InvalidOperationException("האפליקציה אינה משויכת ללומדה.");
        }
        var stepId = "step-" + Guid.NewGuid().ToString("N");
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandTimeout = 3;
        insert.CommandText = @"INSERT INTO Steps
(GuideId, StepId, StepOrder, IsEnabled, ApplicationContextId, BubbleContent, BubblePlacement)
SELECT $guideId, $stepId, COALESCE(MAX(StepOrder), 0) + 1,
1, $contextId, $content, $placement FROM Steps WHERE GuideId=$guideId";
        insert.Parameters.AddWithValue("$guideId", guideId);
        insert.Parameters.AddWithValue("$stepId", stepId);
        insert.Parameters.AddWithValue("$contextId", contextId);
        insert.Parameters.AddWithValue("$content", content);
        insert.Parameters.AddWithValue("$placement", placement);
        if (insert.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("יצירת השלב נכשלה.");
        transaction.Commit();
        return stepId;
    }

    // Duplicate one step and its step-owned associations atomically. Element definitions,
    // anchors, application contexts and guide variables remain shared references.
    internal static string DuplicateStep(long guideId, string sourceStepId)
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
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 3;
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$source", sourceStepId);
        command.CommandText = "SELECT StepOrder FROM Steps WHERE GuideId=$guideId AND StepId=$source";
        var originalOrder = command.ExecuteScalar();
        if (originalOrder is null)
            throw new InvalidOperationException("שלב המקור לא נמצא.");
        // Duplicates are appended at the end; existing step orders never change.
        command.CommandText = "SELECT COALESCE(MAX(StepOrder), 0) + 1 FROM Steps WHERE GuideId=$guideId";
        var nextOrder = Convert.ToInt32(command.ExecuteScalar());
        var newId = "step-" + Guid.NewGuid().ToString("N");
        command.Parameters.AddWithValue("$newId", newId);
        command.Parameters.AddWithValue("$nextOrder", nextOrder);

        // Clone every element used by the step, including recursively referenced anchors.
        // A duplicated step must not share mutable element definitions with its source.
        var elementIds = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        using (var roots = connection.CreateCommand())
        {
            roots.Transaction = transaction;
            roots.CommandText = "SELECT DISTINCT ElementId FROM StepTargets WHERE GuideId=$guideId AND StepId=$source";
            roots.Parameters.AddWithValue("$guideId", guideId);
            roots.Parameters.AddWithValue("$source", sourceStepId);
            using var reader = roots.ExecuteReader();
            while (reader.Read()) pending.Enqueue(reader.GetString(0));
        }
        while (pending.Count > 0)
        {
            var elementId = pending.Dequeue();
            if (!elementIds.Add(elementId)) continue;
            using var anchors = connection.CreateCommand();
            anchors.Transaction = transaction;
            anchors.CommandText = "SELECT AnchorElementId FROM ElementAnchors WHERE GuideId=$guideId AND ElementId=$elementId";
            anchors.Parameters.AddWithValue("$guideId", guideId);
            anchors.Parameters.AddWithValue("$elementId", elementId);
            using var reader = anchors.ExecuteReader();
            while (reader.Read()) pending.Enqueue(reader.GetString(0));
        }
        var elementMap = elementIds.ToDictionary(id => id,
            _ => "element-" + Guid.NewGuid().ToString("N"), StringComparer.Ordinal);
        foreach (var pair in elementMap)
        {
            using var clone = connection.CreateCommand();
            clone.Transaction = transaction;
            clone.CommandText = @"INSERT INTO Elements
(GuideId,ElementId,Runtime,LocatorsJson,FrameContextJson,DisplayName)
SELECT GuideId,$newElement,Runtime,LocatorsJson,FrameContextJson,DisplayName
FROM Elements WHERE GuideId=$guideId AND ElementId=$oldElement";
            clone.Parameters.AddWithValue("$newElement", pair.Value);
            clone.Parameters.AddWithValue("$guideId", guideId);
            clone.Parameters.AddWithValue("$oldElement", pair.Key);
            if (clone.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("לא ניתן לשכפל את הגדרת רכיב היעד.");
        }
        foreach (var pair in elementMap)
        {
            using var anchors = connection.CreateCommand();
            anchors.Transaction = transaction;
            anchors.CommandText = @"SELECT AnchorElementId,Relation,AnchorOrder
FROM ElementAnchors WHERE GuideId=$guideId AND ElementId=$elementId";
            anchors.Parameters.AddWithValue("$guideId", guideId);
            anchors.Parameters.AddWithValue("$elementId", pair.Key);
            var relations = new List<(string AnchorId, string Relation, long Order)>();
            using (var reader = anchors.ExecuteReader())
                while (reader.Read())
                    relations.Add((reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
            foreach (var relation in relations)
            {
                using var clone = connection.CreateCommand();
                clone.Transaction = transaction;
                clone.CommandText = @"INSERT INTO ElementAnchors
(GuideId,ElementId,AnchorElementId,Relation,AnchorOrder)
VALUES ($guideId,$elementId,$anchorId,$relation,$order)";
                clone.Parameters.AddWithValue("$guideId", guideId);
                clone.Parameters.AddWithValue("$elementId", pair.Value);
                clone.Parameters.AddWithValue("$anchorId", elementMap[relation.AnchorId]);
                clone.Parameters.AddWithValue("$relation", relation.Relation);
                clone.Parameters.AddWithValue("$order", relation.Order);
                clone.ExecuteNonQuery();
            }
        }

        // Copy all current step columns without dropping future persisted settings.
        // Only the step identifier and sequence number change.
        foreach (var table in new[] { "Steps", "StepTargets", "StepRules", "StepVariableActions" })
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='" + table + "'";
            if (Convert.ToInt64(command.ExecuteScalar()) == 0)
            {
                if (table == "StepVariableActions") continue;
                throw new InvalidOperationException("טבלת נתוני שלבים חסרה: " + table);
            }
            var columns = new List<string>();
            using (var schema = connection.CreateCommand())
            {
                schema.Transaction = transaction;
                schema.CommandText = "PRAGMA table_info(" + table + ")";
                using var reader = schema.ExecuteReader();
                while (reader.Read()) columns.Add(reader.GetString(1));
            }
            // The supported step tables use composite natural keys; ActionId
            // and UsageId are scoped by StepId and can be retained.
            var quoted = columns.Select(c => "\"" + c.Replace("\"", "\"\"") + "\"").ToArray();
            var expressions = columns.Select(c => c == "StepId" ? "$newId" :
                c == "StepOrder" && table == "Steps" ? "$nextOrder" :
                "\"" + c.Replace("\"", "\"\"") + "\"").ToArray();
            command.CommandText = "INSERT INTO " + table + " (" + string.Join(",", quoted) +
                ") SELECT " + string.Join(",", expressions) + " FROM " + table +
                " WHERE GuideId=$guideId AND StepId=$source";
            var copied = command.ExecuteNonQuery();
            if (table == "Steps" && copied != 1)
                throw new InvalidOperationException("שכפול השלב נכשל.");
            if (table == "StepTargets")
            {
                foreach (var pair in elementMap)
                {
                    // Remap only the new step; the original and other steps remain untouched.
                    using var remap = connection.CreateCommand();
                    remap.Transaction = transaction;
                    remap.CommandText = @"UPDATE StepTargets SET ElementId=$newElement
WHERE GuideId=$guideId AND StepId=$newId AND ElementId=$oldElement";
                    remap.Parameters.AddWithValue("$newElement", pair.Value);
                    remap.Parameters.AddWithValue("$guideId", guideId);
                    remap.Parameters.AddWithValue("$newId", newId);
                    remap.Parameters.AddWithValue("$oldElement", pair.Key);
                    remap.ExecuteNonQuery();
                }
            }
        }
        transaction.Commit();
        return newId;
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

    // Delete only the selected step, its rules and targets, then unreferenced
    // elements. Keep shared elements/anchors and other steps untouched.
    internal static void DeleteStep(long guideId, string stepId)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("מזהה השלב חסר.");
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
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 3;
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        command.CommandText = "SELECT StepOrder FROM Steps WHERE GuideId=$guideId AND StepId=$stepId";
        var originalOrder = command.ExecuteScalar();
        if (originalOrder is null)
            throw new InvalidOperationException("השלב לא נמצא.");
        var order = Convert.ToInt32(originalOrder);
        command.Parameters.AddWithValue("$order", order);

        // Mark candidates before deleting their step targets.
        command.CommandText = @"CREATE TEMP TABLE _DeletedStepElements (ElementId TEXT PRIMARY KEY)";
        command.ExecuteNonQuery();
        command.CommandText = @"INSERT INTO _DeletedStepElements (ElementId)
SELECT DISTINCT ElementId FROM StepTargets WHERE GuideId=$guideId AND StepId=$stepId";
        command.ExecuteNonQuery();

        // Step variable actions reference both Steps and StepTargets.
        command.CommandText = @"SELECT COUNT(*) FROM sqlite_master
            WHERE type='table' AND name='StepVariableActions'";
        if (Convert.ToInt64(command.ExecuteScalar()) > 0)
        {
            command.CommandText = "DELETE FROM StepVariableActions WHERE GuideId=$guideId AND StepId=$stepId";
            command.ExecuteNonQuery();
        }
        command.CommandText = "DELETE FROM StepRules WHERE GuideId=$guideId AND StepId=$stepId";
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM StepTargets WHERE GuideId=$guideId AND StepId=$stepId";
        command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM Steps WHERE GuideId=$guideId AND StepId=$stepId";
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("מחיקת השלב נכשלה.");

        // Clear anchors owned by elements exclusively used by this step.
        // Preserve elements referenced by any surviving step or anchor.
        command.CommandText = @"DELETE FROM ElementAnchors
WHERE GuideId=$guideId AND ElementId IN (
 SELECT e.ElementId FROM _DeletedStepElements e
 WHERE NOT EXISTS (SELECT 1 FROM StepTargets t
   WHERE t.GuideId=$guideId AND t.ElementId=e.ElementId)
 AND NOT EXISTS (SELECT 1 FROM ElementAnchors a
   WHERE a.GuideId=$guideId AND a.AnchorElementId=e.ElementId
     AND a.ElementId NOT IN (SELECT ElementId FROM _DeletedStepElements))
)";
        command.ExecuteNonQuery();
        command.CommandText = @"DELETE FROM Elements
WHERE GuideId=$guideId AND ElementId IN (SELECT ElementId FROM _DeletedStepElements)
AND NOT EXISTS (SELECT 1 FROM StepTargets t
  WHERE t.GuideId=$guideId AND t.ElementId=Elements.ElementId)
AND NOT EXISTS (SELECT 1 FROM ElementAnchors a
  WHERE a.GuideId=$guideId AND
  (a.ElementId=Elements.ElementId OR a.AnchorElementId=Elements.ElementId))";
        command.ExecuteNonQuery();

        // Shift using temporary negative values to satisfy UNIQUE(GuideId,StepOrder).
        command.CommandText = @"UPDATE Steps SET StepOrder=-StepOrder
WHERE GuideId=$guideId AND StepOrder>$order";
        command.ExecuteNonQuery();
        command.CommandText = @"UPDATE Steps SET StepOrder=-StepOrder-1
WHERE GuideId=$guideId AND StepOrder<0";
        command.ExecuteNonQuery();
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
        // Variable tables may be absent until the editor initializes them.
        using (var variableCleanup = connection.CreateCommand())
        {
            variableCleanup.Transaction = transaction;
            variableCleanup.CommandText = @"SELECT name FROM sqlite_master WHERE type='table'
                AND name IN ('StepVariableActions','GuideVariables')";
            var present = new HashSet<string>(StringComparer.Ordinal);
            using (var names = variableCleanup.ExecuteReader())
                while (names.Read()) present.Add(names.GetString(0));
            foreach (var table in new[] { "StepVariableActions", "GuideVariables" })
            {
                if (!present.Contains(table)) continue;
                variableCleanup.CommandText = "DELETE FROM " + table + " WHERE GuideId=$id";
                variableCleanup.Parameters.Clear();
                variableCleanup.Parameters.AddWithValue("$id", guideId);
                variableCleanup.ExecuteNonQuery();
            }
        }
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


    private static void EnsureElementDisplayNameColumn(SqliteConnection connection)
    {
        using var check = connection.CreateCommand();
        check.CommandTimeout = 3;
        check.CommandText = "PRAGMA table_info(Elements)";
        using (var reader = check.ExecuteReader())
            while (reader.Read())
                if (string.Equals(reader.GetString(1), "DisplayName", StringComparison.OrdinalIgnoreCase))
                    return;
        using var migration = connection.CreateCommand();
        migration.CommandTimeout = 3;
        migration.CommandText = "ALTER TABLE Elements ADD COLUMN DisplayName TEXT";
        migration.ExecuteNonQuery();
    }

    private static void EnsureElementTypeColumn(SqliteConnection connection)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(Elements)";
        using var reader = check.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), "ControlType", StringComparison.OrdinalIgnoreCase))
                return;
        reader.Close();
        using var migration = connection.CreateCommand();
        migration.CommandText = "ALTER TABLE Elements ADD COLUMN ControlType TEXT";
        migration.ExecuteNonQuery();
    }

    private static string? CleanDisplayName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : name.Trim()[..Math.Min(name.Trim().Length, 120)];


    // Runtime-neutral primary target writer. Locator discovery belongs to the
    // runtime adapter; V5 persistence and transaction semantics are shared.
    internal sealed record TargetLocator(string Strategy, string Value);
    internal sealed record TargetAnchor(TargetLocator Locator, string Relation);
    internal static void SavePrimaryTargetLocatorTemplate(long guideId, string stepId,
        string locatorValue, string? locatorStrategy = null)
    {
        if (string.IsNullOrWhiteSpace(locatorValue) || locatorValue.Length > 400 ||
            locatorValue.Contains((char)0))
            throw new ArgumentException("מזהה הרכיב אינו תקין.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 3;
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        command.CommandText = @"SELECT e.ElementId, e.LocatorsJson, e.ControlType, e.DisplayName FROM Elements e
JOIN StepTargets t ON t.GuideId=e.GuideId AND t.ElementId=e.ElementId
WHERE t.GuideId=$guideId AND t.StepId=$stepId AND t.Purpose='Primary'";
        string elementId, json;
        string? controlType, displayName;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("לא נמצא רכיב ראשי בשלב.");
            elementId = reader.GetString(0);
            json = reader.GetString(1);
            controlType = reader.IsDBNull(2) ? null : reader.GetString(2);
            displayName = reader.IsDBNull(3) ? null : reader.GetString(3);
        }
        var locators = JsonSerializer.Deserialize<List<TargetLocator>>(json)
            ?? throw new InvalidOperationException("מזהי הרכיב אינם תקינים.");
        if (locators.Count == 0) throw new InvalidOperationException("לא נמצא מזהה רכיב.");
        var original = locators[0];
        // The editor changes only the first locator. Existing frame, anchors,
        // validation rules and other locators remain untouched.
        var matches = System.Text.RegularExpressions.Regex.Matches(
            locatorValue, @"\{\{var:(?<id>[^{}:]+)\}\}");
        var withoutTokens = System.Text.RegularExpressions.Regex.Replace(
            locatorValue, @"\{\{var:[^{}:]+\}\}", "");
        if (withoutTokens.Contains("{{", StringComparison.Ordinal) ||
            withoutTokens.Contains("}}", StringComparison.Ordinal))
            throw new ArgumentException("נמצא סימון משתנה לא תקין.");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            command.Parameters.AddWithValue("$variableId", match.Groups["id"].Value);
            command.CommandText = @"SELECT COUNT(*) FROM GuideVariables
WHERE GuideId=$guideId AND VariableId=$variableId";
            if (Convert.ToInt64(command.ExecuteScalar()) != 1)
                throw new ArgumentException("משתנה במזהה אינו קיים בלומדה.");
            command.Parameters.RemoveAt("$variableId");
        }
        var convertToName = locatorStrategy == "name" && original.Strategy == "automation-id";
        if (locatorStrategy is not null && !convertToName)
            throw new ArgumentException("שינוי אסטרטגיית הזיהוי אינו נתמך.");
        if (convertToName &&
            (controlType is not ("ControlType.Button" or "ControlType.Hyperlink" or
                "ControlType.Text" or "ControlType.DataItem" or "ControlType.ListItem") ||
             string.IsNullOrWhiteSpace(displayName)))
            throw new ArgumentException("לא ניתן להגדיר זיהוי דינמי לרכיב זה.");
        if (!convertToName && original.Strategy is not ("css" or "name"))
            throw new ArgumentException("מזהה טכני אינו ניתן לעריכת תוכן.");
        if (original.Strategy == "css")
        {
            // Variables may change quoted CSS values (including :cell-text),
            // never the selector's structural syntax.
            const string quoted = "\\\"(?:[^\\\"\\\\]|\\\\.)*\\\"|'(?:[^'\\\\]|\\\\.)*'";
            var originalShape = System.Text.RegularExpressions.Regex.Replace(original.Value, quoted, "\"\"");
            var updatedShape = System.Text.RegularExpressions.Regex.Replace(locatorValue, quoted, "\"\"");
            // Adding one supported semantic text filter is allowed for a
            // previously captured locator; no other CSS structure may change.
            var appendedTextFilter = !original.Value.Contains(":exact-text(", StringComparison.Ordinal) &&
                !original.Value.Contains(":cell-text(", StringComparison.Ordinal) &&
                string.Equals(updatedShape, originalShape + ":exact-text(\"\")", StringComparison.Ordinal);
            if (!string.Equals(originalShape, updatedShape, StringComparison.Ordinal) && !appendedTextFilter)
                throw new ArgumentException("ניתן לערוך את ערך התוכן בלבד, לא את מבנה מזהה הרכיב.");
        }
        locators[0] = original with { Strategy = convertToName ? "name" : original.Strategy, Value = locatorValue };
        command.Parameters.AddWithValue("$elementId", elementId);
        command.Parameters.AddWithValue("$locators", JsonSerializer.Serialize(locators));
        command.CommandText = @"UPDATE Elements SET LocatorsJson=$locators
WHERE GuideId=$guideId AND ElementId=$elementId
AND NOT EXISTS (SELECT 1 FROM StepTargets t
WHERE t.GuideId=$guideId AND t.ElementId=$elementId
AND (t.StepId<>$stepId OR t.Purpose<>'Primary'))";
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("הרכיב משותף לשלבים נוספים ולא ניתן לשנות אותו כאן.");
        transaction.Commit();
    }

    internal static void SavePrimaryTarget(long guideId, string stepId, string runtime,
        IReadOnlyList<TargetLocator> locators, IReadOnlyList<TargetLocator> frames,
        string? displayName, string condition, bool replacing,
        IReadOnlyList<(TargetLocator Locator, string Relation)>? anchors = null,
        string? controlType = null)
    {
        if (runtime is not ("Web" or "Windows") || locators.Count is < 1 or > 12 ||
            locators.Any(l => string.IsNullOrWhiteSpace(l.Value) || l.Value.Length > 400 ||
                l.Value.Contains((char)0) || (runtime == "Web"
                  ? l.Strategy != "css" : l.Strategy is not ("automation-id" or "name"))) ||
            frames.Count > 12 || frames.Any(l => runtime != "Web" || l.Strategy != "css" ||
                string.IsNullOrWhiteSpace(l.Value) || l.Value.Length > 400 || l.Value.Contains((char)0)))
            throw new ArgumentException("מזהי הרכיב אינם תקינים.");
        if (anchors is { Count: > 0 } &&
            (runtime != "Windows" || anchors.Count > 12 ||
             anchors.Any(a => a.Relation is not ("Sibling" or "Ancestor") ||
                 a.Locator.Strategy is not ("name" or "automation-id") ||
                 string.IsNullOrWhiteSpace(a.Locator.Value) || a.Locator.Value.Length > 400)))
            throw new ArgumentException("עוגני הרכיב אינם תקינים.");
        if (!replacing && condition is not ("" or "clicked" or "target-disappeared" or "value-not-empty"))
            throw new ArgumentException("תנאי ההתקדמות אינו נתמך.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        EnsureElementDisplayNameColumn(connection);
        EnsureElementTypeColumn(connection);
        using (var fk = connection.CreateCommand())
        {
            fk.CommandText = "PRAGMA foreign_keys=ON";
            fk.ExecuteNonQuery();
        }
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 3;
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        command.Parameters.AddWithValue("$runtime", runtime);
        command.CommandText = @"SELECT COUNT(*) FROM Steps s JOIN ApplicationContexts c
ON c.GuideId=s.GuideId AND c.ContextId=s.ApplicationContextId
WHERE s.GuideId=$guideId AND s.StepId=$stepId AND c.Runtime=$runtime";
        if (Convert.ToInt64(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException("השלב אינו משויך לאפליקציה בסביבת היעד שנבחרה.");
        command.CommandText = @"SELECT ElementId FROM StepTargets
WHERE GuideId=$guideId AND StepId=$stepId AND Purpose='Primary'";
        var existingId = command.ExecuteScalar()?.ToString();
        if (replacing && string.IsNullOrEmpty(existingId))
            throw new InvalidOperationException("לא נמצא רכיב ראשי להחלפה.");
        if (!replacing)
        {
            command.CommandText = @"SELECT COUNT(*) FROM StepTargets
WHERE GuideId=$guideId AND StepId=$stepId AND Purpose='Primary'";
            if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                throw new InvalidOperationException("כבר קיים רכיב יעד בשלב.");
            command.CommandText = @"SELECT COUNT(*) FROM StepRules
WHERE GuideId=$guideId AND StepId=$stepId";
            if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                throw new InvalidOperationException("כבר קיימים כללי התקדמות בשלב.");
        }
        var elementId = replacing ? existingId! : "element-" + Guid.NewGuid().ToString("N");
        command.Parameters.AddWithValue("$elementId", elementId);
        command.Parameters.AddWithValue("$locators", JsonSerializer.Serialize(locators));
        command.Parameters.AddWithValue("$frame", frames.Count == 0
            ? (object)DBNull.Value : JsonSerializer.Serialize(frames));
        command.Parameters.AddWithValue("$displayName", (object?)CleanDisplayName(displayName) ?? DBNull.Value);
        command.Parameters.AddWithValue("$controlType", (object?)controlType ?? DBNull.Value);
        if (replacing)
        {
            // A new primary control invalidates the old primary validation.
            // Keep independent additional completion conditions untouched.
            command.CommandText = @"DELETE FROM StepRules
WHERE GuideId=$guideId AND StepId=$stepId AND RuleType='PrimaryValidation'";
            command.ExecuteNonQuery();
            // Replacing the action invalidates its previously captured identifier.
            // Never keep an anchor bound to an unrelated newly selected control.
            command.CommandText = @"DELETE FROM ElementAnchors
                WHERE GuideId=$guideId AND ElementId=$elementId";
            command.ExecuteNonQuery();
            command.CommandText = @"UPDATE Elements SET LocatorsJson=$locators,
FrameContextJson=$frame, DisplayName=$displayName, ControlType=$controlType
WHERE GuideId=$guideId AND ElementId=$elementId AND Runtime=$runtime";
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("עדכון הרכיב נכשל.");
        }
        else
        {
            command.CommandText = @"INSERT INTO Elements
(GuideId, ElementId, Runtime, LocatorsJson, FrameContextJson, DisplayName, ControlType)
VALUES ($guideId, $elementId, $runtime, $locators, $frame, $displayName, $controlType)";
            command.ExecuteNonQuery();
            command.CommandText = @"INSERT INTO StepTargets
(GuideId, StepId, UsageId, ElementId, Purpose)
VALUES ($guideId, $stepId, 'primary', $elementId, 'Primary')";
            command.ExecuteNonQuery();
            if (!string.IsNullOrEmpty(condition))
            {
                command.Parameters.AddWithValue("$condition", condition);
                command.CommandText = @"INSERT INTO StepRules
(GuideId, StepId, RuleType, RuleId, Kind, TargetUsageId)
VALUES ($guideId, $stepId, 'PrimaryValidation', 'primary', $condition, 'primary')";
                command.ExecuteNonQuery();
            }
            command.CommandText = @"UPDATE Steps SET BubblePlacement='Auto'
WHERE GuideId=$guideId AND StepId=$stepId AND BubblePlacement='Center'";
            command.ExecuteNonQuery();
        }
        if (anchors is not null)
        {
            for (var index = 0; index < anchors.Count; index++)
            {
                var anchor = anchors[index];
                var anchorId = "element-" + Guid.NewGuid().ToString("N");
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandTimeout = 3;
                insert.CommandText = @"INSERT INTO Elements
(GuideId,ElementId,Runtime,LocatorsJson,FrameContextJson,DisplayName)
VALUES ($guideId,$anchorId,$runtime,$locators,NULL,NULL)";
                insert.Parameters.AddWithValue("$guideId", guideId);
                insert.Parameters.AddWithValue("$anchorId", anchorId);
                insert.Parameters.AddWithValue("$runtime", runtime);
                insert.Parameters.AddWithValue("$locators", JsonSerializer.Serialize(new[] { anchor.Locator }));
                insert.ExecuteNonQuery();
                insert.CommandText = @"INSERT INTO ElementAnchors
(GuideId,ElementId,AnchorElementId,Relation,AnchorOrder)
VALUES ($guideId,$elementId,$anchorId,$relation,$order)";
                insert.Parameters.AddWithValue("$elementId", elementId);
                insert.Parameters.AddWithValue("$relation", anchor.Relation);
                insert.Parameters.AddWithValue("$order", index);
                insert.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    // Remove the step's target-dependent configuration without deleting the step or shared elements.
    internal static void RemovePrimaryTarget(long guideId, string stepId)
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
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 3;
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        command.CommandText = @"SELECT COUNT(*) FROM StepTargets
WHERE GuideId=$guideId AND StepId=$stepId AND Purpose='Primary'";
        if (Convert.ToInt64(command.ExecuteScalar()) == 0)
            throw new InvalidOperationException("לא נמצא רכיב להסרה.");

        command.CommandText = @"CREATE TEMP TABLE _RemovedTargetElements (ElementId TEXT PRIMARY KEY)";
        command.ExecuteNonQuery();
        command.CommandText = @"INSERT INTO _RemovedTargetElements (ElementId)
SELECT DISTINCT ElementId FROM StepTargets WHERE GuideId=$guideId AND StepId=$stepId";
        command.ExecuteNonQuery();

        command.CommandText = @"SELECT COUNT(*) FROM sqlite_master
WHERE type='table' AND name='StepVariableActions'";
        if (Convert.ToInt64(command.ExecuteScalar()) > 0)
        {
            // Every variable action reads a target; removing the primary target
            // clears step captures so none retain a detached source.
            command.CommandText = @"DELETE FROM StepVariableActions
WHERE GuideId=$guideId AND StepId=$stepId";
            command.ExecuteNonQuery();
        }
        command.CommandText = @"DELETE FROM StepRules WHERE GuideId=$guideId AND StepId=$stepId";
        command.ExecuteNonQuery();
        command.CommandText = @"DELETE FROM StepTargets WHERE GuideId=$guideId AND StepId=$stepId";
        command.ExecuteNonQuery();
        command.CommandText = @"DELETE FROM ElementAnchors WHERE GuideId=$guideId
AND ElementId IN (SELECT ElementId FROM _RemovedTargetElements)
AND NOT EXISTS (SELECT 1 FROM StepTargets t
  WHERE t.GuideId=$guideId AND t.ElementId=ElementAnchors.ElementId)";
        command.ExecuteNonQuery();
        command.CommandText = @"DELETE FROM Elements WHERE GuideId=$guideId
AND ElementId IN (SELECT ElementId FROM _RemovedTargetElements)
AND NOT EXISTS (SELECT 1 FROM StepTargets t
  WHERE t.GuideId=$guideId AND t.ElementId=Elements.ElementId)
AND NOT EXISTS (SELECT 1 FROM ElementAnchors a
  WHERE a.GuideId=$guideId AND
  (a.ElementId=Elements.ElementId OR a.AnchorElementId=Elements.ElementId))";
        command.ExecuteNonQuery();
        command.CommandText = @"UPDATE Steps SET BubblePlacement='Center'
WHERE GuideId=$guideId AND StepId=$stepId";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    // An explicitly selected secondary target, never inferred from the primary action.
    internal static void SaveWebCompletionTarget(long guideId, string stepId, string selector,
        IReadOnlyList<string> framePath, string kind, string? displayName,
        string? attributeName = null, string? attributeValue = null,
        string? variableId = null, string? selectedTag = null, string? expectedValue = null,
        string runtime = "Web", IReadOnlyList<TargetLocator>? windowsLocators = null,
        IReadOnlyList<TargetAnchor>? windowsAnchors = null)
    {
        if (runtime is not ("Web" or "Windows"))
            throw new ArgumentException("סביבת יעד לא נתמכת.");
        if (runtime == "Windows" && (windowsLocators is null || windowsLocators.Count == 0 ||
            windowsLocators.Any(l => l.Strategy is not ("automation-id" or "name") || string.IsNullOrWhiteSpace(l.Value) || l.Value.Length > 400)))
            throw new ArgumentException("מזהה רכיב Windows אינו תקין.");
        if (windowsAnchors is { Count: > 0 } &&
            (runtime != "Windows" || windowsAnchors.Count > 12 ||
             windowsAnchors.Any(a => a.Relation is not ("Sibling" or "Ancestor") ||
                 a.Locator.Strategy is not ("name" or "automation-id") ||
                 string.IsNullOrWhiteSpace(a.Locator.Value) || a.Locator.Value.Length > 400)))
            throw new ArgumentException("עוגני רכיב ההשלמה אינם תקינים.");
        if (runtime == "Windows" && kind is not ("value-contains" or "target-appeared" or "target-vanished"))
            throw new ArgumentException("תנאי זה עדיין אינו נתמך עבור Windows.");
        if (kind is not ("target-appeared" or "target-vanished" or "attribute-equals" or "value-contains"))
            throw new ArgumentException("חוק השלמה לא נתמך.");
        if (kind == "value-contains" && (string.IsNullOrWhiteSpace(expectedValue) || expectedValue.Length > 500))
            throw new ArgumentException("יש להזין ערך צפוי לתנאי ההשלמה.");
        if (kind == "attribute-equals" &&
            (string.IsNullOrWhiteSpace(attributeName) ||
             !System.Text.RegularExpressions.Regex.IsMatch(attributeName, @"^[a-zA-Z_][a-zA-Z0-9_.:-]{0,99}$") ||
             attributeValue is null || attributeValue.Length > 500))
            throw new ArgumentException("יש להזין שם מאפיין וערך צפוי תקינים.");
        if ((runtime == "Web" && string.IsNullOrWhiteSpace(selector)) || selector.Length > 400 ||
            selector.IndexOf((char)0) >= 0 || framePath.Count > 12 ||
            framePath.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 400))
            throw new ArgumentException("רכיב תנאי ההשלמה אינו תקין.");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        EnsureElementDisplayNameColumn(connection);
        // Validate every variable reference in the unified expected-value template.
        if (kind == "value-contains" && expectedValue is not null)
        {
            var tokens = System.Text.RegularExpressions.Regex.Matches(
                expectedValue, @"\{\{var:(?<id>[^{}:]+)\}\}");
            var plain = System.Text.RegularExpressions.Regex.Replace(
                expectedValue, @"\{\{var:[^{}:]+\}\}", "");
            if (plain.Contains("{{", StringComparison.Ordinal) ||
                plain.Contains("}}", StringComparison.Ordinal))
                throw new ArgumentException("סימון משתנה לא תקין.");
            using var variableCheck = connection.CreateCommand();
            variableCheck.CommandText = "SELECT COUNT(*) FROM GuideVariables WHERE GuideId=$guide AND VariableId=$variable";
            variableCheck.Parameters.AddWithValue("$guide", guideId);
            var parameter = variableCheck.Parameters.Add("$variable", SqliteType.Text);
            foreach (System.Text.RegularExpressions.Match token in tokens)
            {
                parameter.Value = token.Groups["id"].Value;
                if (Convert.ToInt64(variableCheck.ExecuteScalar()) != 1)
                    throw new ArgumentException("המשתנה אינו מוגדר בלומדה.");
            }
        }
        using var transaction = connection.BeginTransaction();
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = @"SELECT COUNT(*) FROM Steps s
JOIN ApplicationContexts c ON c.GuideId=s.GuideId AND c.ContextId=s.ApplicationContextId
WHERE s.GuideId=$guideId AND s.StepId=$stepId AND c.Runtime=$runtime
AND EXISTS (SELECT 1 FROM StepTargets t WHERE t.GuideId=s.GuideId
AND t.StepId=s.StepId AND t.Purpose='Primary')
AND EXISTS (SELECT 1 FROM StepRules r WHERE r.GuideId=s.GuideId
AND r.StepId=s.StepId AND r.RuleType='PrimaryValidation')";
        check.Parameters.AddWithValue("$guideId", guideId);
        check.Parameters.AddWithValue("$stepId", stepId);
        check.Parameters.AddWithValue("$runtime", runtime);
        if (Convert.ToInt64(check.ExecuteScalar()) != 1)
            throw new InvalidOperationException("יש להגדיר קודם רכיב פעולה ותנאי ראשי.");
        check.CommandText = @"SELECT COUNT(*) FROM StepRules WHERE GuideId=$guideId
AND StepId=$stepId AND RuleType='AdditionalCompletionCondition'";
        if (Convert.ToInt64(check.ExecuteScalar()) != 0)
            throw new InvalidOperationException("כבר קיים תנאי השלמה נוסף. יש להסיר אותו לפני בחירת אחר.");

        var elementId = "element-" + Guid.NewGuid().ToString("N");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"INSERT INTO Elements
(GuideId, ElementId, Runtime, LocatorsJson, FrameContextJson, DisplayName)
VALUES ($guideId, $elementId, $runtime, $locators, $frame, $name)";
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        command.Parameters.AddWithValue("$runtime", runtime);
        command.Parameters.AddWithValue("$elementId", elementId);
        command.Parameters.AddWithValue("$locators", JsonSerializer.Serialize(
            runtime == "Web" ? (IReadOnlyList<TargetLocator>)new[] { new TargetLocator("css", selector) } : windowsLocators!));
        command.Parameters.AddWithValue("$frame", framePath.Count == 0 ? DBNull.Value :
            JsonSerializer.Serialize(framePath.Select(p => new { Strategy="css", Value=p })));
        command.Parameters.AddWithValue("$name", (object?)CleanDisplayName(displayName) ?? DBNull.Value);
        command.ExecuteNonQuery();
        if (windowsAnchors is { Count: > 0 })
        {
            for (var index = 0; index < windowsAnchors.Count; index++)
            {
                var anchor = windowsAnchors[index];
                var anchorId = "element-" + Guid.NewGuid().ToString("N");
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"INSERT INTO Elements
(GuideId,ElementId,Runtime,LocatorsJson,FrameContextJson,DisplayName)
VALUES ($guideId,$anchorId,$runtime,$locators,NULL,NULL)";
                insert.Parameters.AddWithValue("$guideId", guideId);
                insert.Parameters.AddWithValue("$anchorId", anchorId);
                insert.Parameters.AddWithValue("$runtime", runtime);
                insert.Parameters.AddWithValue("$locators", JsonSerializer.Serialize(new[] { anchor.Locator }));
                insert.ExecuteNonQuery();
                insert.CommandText = @"INSERT INTO ElementAnchors
(GuideId,ElementId,AnchorElementId,Relation,AnchorOrder)
VALUES ($guideId,$elementId,$anchorId,$relation,$order)";
                insert.Parameters.AddWithValue("$elementId", elementId);
                insert.Parameters.AddWithValue("$relation", anchor.Relation);
                insert.Parameters.AddWithValue("$order", index);
                insert.ExecuteNonQuery();
            }
        }
        command.CommandText = @"INSERT INTO StepTargets
(GuideId, StepId, UsageId, ElementId, Purpose)
VALUES ($guideId, $stepId, 'completion', $elementId, 'Completion')";
        command.ExecuteNonQuery();
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$expected", kind == "attribute-equals"
            ? (object)JsonSerializer.Serialize(new { name = attributeName, value = attributeValue })
            : kind == "value-contains" ? expectedValue! : DBNull.Value);
        command.CommandText = @"INSERT INTO StepRules
(GuideId, StepId, RuleType, RuleId, Kind, TargetUsageId, ExpectedValue)
VALUES ($guideId, $stepId, 'AdditionalCompletionCondition', 'completion', $kind, 'completion', $expected)";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    internal static void RemoveWebCompletionTarget(long guideId, string stepId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        command.CommandText = @"SELECT ElementId FROM StepTargets WHERE GuideId=$guideId
AND StepId=$stepId AND UsageId='completion' AND Purpose='Completion'";
        var elementId = command.ExecuteScalar()?.ToString();
        if (elementId is null) throw new InvalidOperationException("לא נמצא תנאי השלמה נוסף.");
        command.CommandText = @"DELETE FROM StepRules WHERE GuideId=$guideId
AND StepId=$stepId AND RuleType='AdditionalCompletionCondition' AND TargetUsageId='completion'";
        command.ExecuteNonQuery();
        command.CommandText = @"DELETE FROM StepTargets WHERE GuideId=$guideId
AND StepId=$stepId AND UsageId='completion' AND Purpose='Completion'";
        command.ExecuteNonQuery();
        command.Parameters.AddWithValue("$elementId", elementId);
        // Completion targets may own Windows UIA anchors. Remove their links
        // before deleting the target element, then collect unreferenced anchors.
        command.CommandText = @"DELETE FROM ElementAnchors
WHERE GuideId=$guideId AND ElementId=$elementId";
        command.ExecuteNonQuery();
        command.CommandText = @"DELETE FROM Elements WHERE GuideId=$guideId AND ElementId=$elementId
AND NOT EXISTS (SELECT 1 FROM StepTargets WHERE GuideId=$guideId AND ElementId=$elementId)";
        command.ExecuteNonQuery();
        command.CommandText = @"DELETE FROM Elements WHERE GuideId=$guideId
AND NOT EXISTS (SELECT 1 FROM StepTargets t
    WHERE t.GuideId=Elements.GuideId AND t.ElementId=Elements.ElementId)
AND NOT EXISTS (SELECT 1 FROM ElementAnchors a
    WHERE a.GuideId=Elements.GuideId
      AND (a.ElementId=Elements.ElementId OR a.AnchorElementId=Elements.ElementId))";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    internal static void UpdatePrimaryValidation(long guideId, string stepId,
        string kind, string? expectedValue)
    {
        if (kind is not ("clicked" or "target-disappeared" or "value-not-empty" or "value-equals"))
            throw new ArgumentException("סוג תנאי ההתקדמות אינו נתמך.");
        if (kind == "value-equals" && string.IsNullOrWhiteSpace(expectedValue))
            throw new ArgumentException("יש להזין ערך צפוי.");
        if (expectedValue?.Length > 500)
            throw new ArgumentException("הערך הצפוי ארוך מדי.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 3;
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$expected",
            kind == "value-equals" ? (object?)expectedValue : DBNull.Value);
        command.CommandText = @"SELECT COUNT(*) FROM Steps s
JOIN ApplicationContexts c ON s.GuideId=c.GuideId AND s.ApplicationContextId=c.ContextId
JOIN StepTargets t ON t.GuideId=s.GuideId AND t.StepId=s.StepId AND t.Purpose='Primary'
WHERE s.GuideId=$guideId AND s.StepId=$stepId AND c.Runtime IN ('Web','Windows')";
        if (Convert.ToInt64(command.ExecuteScalar()) != 1)
            throw new InvalidOperationException("יש לבחור רכיב לפני הגדרת תנאי התקדמות.");
        command.CommandText = @"SELECT COUNT(*) FROM StepRules
WHERE GuideId=$guideId AND StepId=$stepId AND RuleType='PrimaryValidation'";
        var existing = Convert.ToInt64(command.ExecuteScalar());
        if (existing > 1)
            throw new InvalidOperationException("נמצאו מספר תנאי התקדמות ראשיים.");
        command.CommandText = existing == 0
            ? @"INSERT INTO StepRules
(GuideId,StepId,RuleType,RuleId,Kind,TargetUsageId,ExpectedValue)
VALUES ($guideId,$stepId,'PrimaryValidation','primary',$kind,'primary',$expected)"
            : @"UPDATE StepRules SET Kind=$kind, ExpectedValue=$expected
WHERE GuideId=$guideId AND StepId=$stepId AND RuleType='PrimaryValidation'";
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("שמירת תנאי ההתקדמות נכשלה.");
        transaction.Commit();
    }

    internal static void RemovePrimaryValidation(long guideId, string stepId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 3;
        command.CommandText = @"DELETE FROM StepRules
WHERE GuideId=$guideId AND StepId=$stepId AND RuleType='PrimaryValidation'";
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        command.ExecuteNonQuery();
    }

    // Edit only fields already represented by the V5 Steps entity.
    // Never recreate a step or overwrite its targets, rules or captures.
    internal static void UpdateStep(long guideId, string stepId, string? bubble, bool enabled, string? contextId)
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
SET BubbleContent=$bubble, IsEnabled=$enabled,
    ApplicationContextId=COALESCE($contextId, ApplicationContextId)
WHERE GuideId=$guideId AND StepId=$stepId
  AND ($contextId IS NULL OR EXISTS (SELECT 1 FROM ApplicationContexts
                 WHERE GuideId=$guideId AND ContextId=$contextId))";
        command.Parameters.AddWithValue("$bubble", (object?)bubble ?? DBNull.Value);
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$contextId", string.IsNullOrWhiteSpace(contextId) ? DBNull.Value : contextId);
        command.Parameters.AddWithValue("$guideId", guideId);
        command.Parameters.AddWithValue("$stepId", stepId);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("השלב לא נמצא.");
    }

    internal static void ReorderSteps(long guideId, IReadOnlyList<string> stepIds)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 3;
        command.CommandText = "SELECT StepId FROM Steps WHERE GuideId=$guideId";
        command.Parameters.AddWithValue("$guideId", guideId);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) existing.Add(reader.GetString(0));
        if (stepIds is null || stepIds.Count != existing.Count ||
            stepIds.Distinct(StringComparer.Ordinal).Count() != existing.Count ||
            stepIds.Any(id => !existing.Contains(id)))
            throw new ArgumentException("רשימת השלבים אינה תואמת ללומדה.");
        command.CommandText = "UPDATE Steps SET StepOrder=$order WHERE GuideId=$guideId AND StepId=$stepId";
        command.Parameters.Add("$order", Microsoft.Data.Sqlite.SqliteType.Integer);
        command.Parameters.Add("$stepId", Microsoft.Data.Sqlite.SqliteType.Text);
        // The schema enforces UNIQUE (GuideId, StepOrder). Move all rows to
        // temporary negative positions before assigning the final positive order.
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i < stepIds.Count; i++)
            {
                command.Parameters["$order"].Value = pass == 0 ? -(i + 1) : i + 1;
                command.Parameters["$stepId"].Value = stepIds[i];
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("עדכון סדר השלבים נכשל.");
            }
        transaction.Commit();
    }

}
