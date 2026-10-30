using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace GuideMe.Instructor.Web.Data;

/// <summary>Guide-level variable definitions. Values belong to Learner sessions.</summary>
internal static class V5VariableCatalog
{
    internal sealed record Variable(string Id, string Name, string? InitialValue);
    private static SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = V5GuideReader.DatabasePath, Mode = SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 3
        }.ToString());
        connection.Open();
        using var fk = connection.CreateCommand();
        fk.CommandText = "PRAGMA foreign_keys=ON";
        fk.ExecuteNonQuery();
        return connection;
    }

    // Explicitly requested during editing, never when publishing/starting Learner.
    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"CREATE TABLE IF NOT EXISTS GuideVariables (
            GuideId INTEGER NOT NULL, VariableId TEXT NOT NULL,
            DisplayName TEXT NOT NULL, InitialValue TEXT,
            PRIMARY KEY (GuideId, VariableId),
            UNIQUE (GuideId, DisplayName),
            FOREIGN KEY (GuideId) REFERENCES Guides(GuideId)
        )";
        command.ExecuteNonQuery();
        command.CommandText = @"CREATE TABLE IF NOT EXISTS StepVariableActions (
            GuideId INTEGER NOT NULL, StepId TEXT NOT NULL, ActionId TEXT NOT NULL,
            ActionOrder INTEGER NOT NULL, VariableId TEXT NOT NULL,
            ActionKind TEXT NOT NULL CHECK (ActionKind IN
                ('ReadFromTarget')),
            TargetUsageId TEXT,
            Property TEXT, Pattern TEXT,
            Timing TEXT NOT NULL DEFAULT 'AfterAction',
            PRIMARY KEY (GuideId,StepId,ActionId),
            UNIQUE (GuideId,StepId,ActionOrder),
            FOREIGN KEY (GuideId,StepId) REFERENCES Steps(GuideId,StepId),
            FOREIGN KEY (GuideId,VariableId) REFERENCES GuideVariables(GuideId,VariableId),
            FOREIGN KEY (GuideId,StepId,TargetUsageId)
                REFERENCES StepTargets(GuideId,StepId,UsageId)
        )";
        command.ExecuteNonQuery();
    }

    internal static IReadOnlyList<Variable> List(long guideId)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT VariableId,DisplayName,InitialValue FROM GuideVariables WHERE GuideId=$guide ORDER BY rowid";
        command.Parameters.AddWithValue("$guide", guideId);
        using var reader = command.ExecuteReader();
        var result = new List<Variable>();
        while (reader.Read())
            result.Add(new Variable(reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        return result;
    }

    internal static Variable Add(long guideId, string name, string? initialValue)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 1 or > 80)
            throw new ArgumentException("שם המשתנה חייב להכיל בין 1 ל־80 תווים.");
        if (initialValue?.Length > 1000)
            throw new ArgumentException("הערך ההתחלתי ארוך מדי.");
        using var connection = Open();
        EnsureSchema(connection);
        var id = "var-" + Guid.NewGuid().ToString("N");
        using var command = connection.CreateCommand();
        command.CommandText = @"INSERT INTO GuideVariables (GuideId,VariableId,DisplayName,InitialValue)
            VALUES ($guide,$id,$name,$value)";
        command.Parameters.AddWithValue("$guide", guideId);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$value", (object?)initialValue ?? DBNull.Value);
        try { command.ExecuteNonQuery(); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        { throw new InvalidOperationException("הלומדה אינה קיימת או ששם המשתנה כבר בשימוש.", e); }
        return new Variable(id, name, initialValue);
    }

    internal static void Update(long guideId, string variableId, string name, string? initialValue)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 1 or > 80 || initialValue?.Length > 1000)
            throw new ArgumentException("פרטי המשתנה אינם תקינים.");
        using var connection = Open();
        EnsureSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE GuideVariables SET DisplayName=$name,InitialValue=$value
            WHERE GuideId=$guide AND VariableId=$id";
        command.Parameters.AddWithValue("$guide", guideId);
        command.Parameters.AddWithValue("$id", variableId);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$value", (object?)initialValue ?? DBNull.Value);
        try { if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("המשתנה לא נמצא."); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        { throw new InvalidOperationException("שם המשתנה כבר בשימוש.", e); }
    }

    internal static void Delete(long guideId, string variableId)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT COUNT(*) FROM Elements WHERE GuideId=$guide
            AND (LocatorsJson LIKE $token OR FrameContextJson LIKE $token)";
        command.Parameters.AddWithValue("$token", "%{{var:" + variableId + "}}%");
        command.Parameters.AddWithValue("$guide", guideId);
        if (Convert.ToInt64(command.ExecuteScalar()) != 0)
            throw new InvalidOperationException("לא ניתן למחוק משתנה המשמש לזיהוי רכיב. יש להסיר תחילה את הקישור.");
        command.CommandText = @"DELETE FROM GuideVariables WHERE GuideId=$guide AND VariableId=$id";
        command.Parameters.AddWithValue("$id", variableId);
        try { if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("המשתנה לא נמצא."); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        { throw new InvalidOperationException("לא ניתן למחוק משתנה שמשמש בשלבי הלומדה.", e); }
    }
    internal sealed record Action(string Id, int Order, string VariableId, string Kind,
        string Timing,
        string? TargetUsageId, string? Property, string? Pattern);

    internal static IReadOnlyList<Action> ListActions(long guideId, string stepId)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT ActionId,ActionOrder,VariableId,ActionKind,
            Timing,TargetUsageId,Property,Pattern FROM StepVariableActions
            WHERE GuideId=$guide AND StepId=$step ORDER BY ActionOrder";
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        using var reader = cmd.ExecuteReader();
        var result = new List<Action>();
        while (reader.Read())
            result.Add(new Action(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        return result;
    }

    /// <summary>Persist a picked, independent source target and its read action
    /// in one transaction; neither the primary target nor completion rules change.</summary>
    internal static void AddPickedRead(long guideId, string stepId, string variableId,
        string runtime, IReadOnlyList<V5GuideWriter.TargetLocator> locators,
        IReadOnlyList<V5GuideWriter.TargetLocator> frames, string? displayName,
        string property, string? pattern)
    {
        if (pattern is { Length: > 300 })
            throw new ArgumentException("כלל חילוץ ארוך מדי.");
        if (!string.IsNullOrEmpty(pattern))
        {
            try { _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250)); }
            catch (ArgumentException) { throw new ArgumentException("כלל החילוץ אינו תקין."); }
        }
        if (runtime is not ("Web" or "Windows") || locators.Count is < 1 or > 12
            || locators.Any(l => string.IsNullOrWhiteSpace(l.Value) || l.Value.Length > 400
                || (runtime == "Web" ? l.Strategy != "css"
                    : l.Strategy is not ("name" or "automation-id")))
            || frames.Count > 12 || frames.Any(l => runtime != "Web"
                || l.Strategy != "css" || string.IsNullOrWhiteSpace(l.Value)))
            throw new ArgumentException("מזהה רכיב המקור אינו תקין.");
        if (runtime == "Web" && property is not ("text" or "value" or "frame-url-fragment")
            || runtime == "Windows" && property is not ("name" or "automation-id"))
            throw new ArgumentException("מאפיין קריאה אינו נתמך.");
        using var connection = Open();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT COUNT(*) FROM Steps s
            JOIN ApplicationContexts c ON c.GuideId=s.GuideId
                AND c.ContextId=s.ApplicationContextId
            JOIN GuideVariables v ON v.GuideId=s.GuideId AND v.VariableId=$variable
            WHERE s.GuideId=$guide AND s.StepId=$step AND c.Runtime=$runtime";
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        cmd.Parameters.AddWithValue("$variable", variableId);
        cmd.Parameters.AddWithValue("$runtime", runtime);
        if (Convert.ToInt64(cmd.ExecuteScalar()) != 1)
            throw new InvalidOperationException("הרכיב או המשתנה אינם שייכים לשלב באפליקציית היעד.");
        var usageId = "variable-" + Guid.NewGuid().ToString("N");
        var elementId = "element-" + Guid.NewGuid().ToString("N");
        cmd.Parameters.AddWithValue("$usage", usageId);
        cmd.Parameters.AddWithValue("$element", elementId);
        cmd.Parameters.AddWithValue("$locators", JsonSerializer.Serialize(locators));
        cmd.Parameters.AddWithValue("$frames", frames.Count == 0
            ? (object)DBNull.Value : JsonSerializer.Serialize(frames));
        cmd.Parameters.AddWithValue("$name", (object?)displayName ?? DBNull.Value);
        cmd.CommandText = @"INSERT INTO Elements
            (GuideId,ElementId,Runtime,LocatorsJson,FrameContextJson,DisplayName)
            VALUES ($guide,$element,$runtime,$locators,$frames,$name)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = @"INSERT INTO StepTargets
            (GuideId,StepId,UsageId,ElementId,Purpose)
            VALUES ($guide,$step,$usage,$element,'VariableSource')";
        cmd.ExecuteNonQuery();
        cmd.CommandText = @"INSERT INTO StepVariableActions
            (GuideId,StepId,ActionId,ActionOrder,VariableId,ActionKind,
             TargetUsageId,Property,Timing,Pattern)
            VALUES ($guide,$step,$action,
                (SELECT COALESCE(MAX(ActionOrder),-1)+1 FROM StepVariableActions
                 WHERE GuideId=$guide AND StepId=$step),
                $variable,'ReadFromTarget',$usage,$property,$timing,$pattern)";
        cmd.Parameters.AddWithValue("$action", "action-" + Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$property", property);
        cmd.Parameters.AddWithValue("$timing", "AfterAction");
        cmd.Parameters.AddWithValue("$pattern", (object?)pattern ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    internal static void DeleteAction(long guideId, string stepId, string actionId)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        cmd.Parameters.AddWithValue("$id", actionId);
        cmd.CommandText = @"SELECT TargetUsageId FROM StepVariableActions
            WHERE GuideId=$guide AND StepId=$step AND ActionId=$id";
        var usageId = cmd.ExecuteScalar() as string;
        cmd.CommandText = @"DELETE FROM StepVariableActions
            WHERE GuideId=$guide AND StepId=$step AND ActionId=$id";
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("פעולת המשתנה לא נמצאה.");
        if (usageId is not null)
        {
            cmd.Parameters.AddWithValue("$usage", usageId);
            cmd.CommandText = @"SELECT ElementId FROM StepTargets
                WHERE GuideId=$guide AND StepId=$step
                    AND UsageId=$usage AND Purpose='VariableSource'
                    AND NOT EXISTS (SELECT 1 FROM StepVariableActions a
                        WHERE a.GuideId=$guide AND a.StepId=$step
                            AND a.TargetUsageId=$usage)";
            var elementId = cmd.ExecuteScalar() as string;
            if (elementId is not null)
            {
                cmd.Parameters.AddWithValue("$element", elementId);
                cmd.CommandText = @"DELETE FROM StepTargets
                    WHERE GuideId=$guide AND StepId=$step AND UsageId=$usage
                        AND Purpose='VariableSource'";
                cmd.ExecuteNonQuery();
                cmd.CommandText = @"DELETE FROM Elements
                    WHERE GuideId=$guide AND ElementId=$element
                    AND NOT EXISTS (SELECT 1 FROM StepTargets t
                        WHERE t.GuideId=$guide AND t.ElementId=$element)";
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    internal static bool HasActions(long guideId)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT EXISTS(SELECT 1 FROM StepVariableActions WHERE GuideId=$guide)";
        command.Parameters.AddWithValue("$guide", guideId);
        return Convert.ToInt64(command.ExecuteScalar()) != 0;
    }

}
