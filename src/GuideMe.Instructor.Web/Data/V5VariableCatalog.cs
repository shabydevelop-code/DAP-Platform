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
                ('SetValue','ReadFromTarget','Clear','CopyFromVariable')),
            LiteralValue TEXT, SourceVariableId TEXT, TargetUsageId TEXT,
            Property TEXT, Pattern TEXT,
            Timing TEXT NOT NULL DEFAULT 'AfterAction',
            PRIMARY KEY (GuideId,StepId,ActionId),
            UNIQUE (GuideId,StepId,ActionOrder),
            FOREIGN KEY (GuideId,StepId) REFERENCES Steps(GuideId,StepId),
            FOREIGN KEY (GuideId,VariableId) REFERENCES GuideVariables(GuideId,VariableId),
            FOREIGN KEY (GuideId,SourceVariableId) REFERENCES GuideVariables(GuideId,VariableId),
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
        string? LiteralValue, string? SourceVariableId, string Timing,
        string? TargetUsageId, string? Property);

    internal static IReadOnlyList<Action> ListActions(long guideId, string stepId)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT ActionId,ActionOrder,VariableId,ActionKind,LiteralValue,
            SourceVariableId,Timing,TargetUsageId,Property FROM StepVariableActions
            WHERE GuideId=$guide AND StepId=$step ORDER BY ActionOrder";
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        using var reader = cmd.ExecuteReader();
        var result = new List<Action>();
        while (reader.Read())
            result.Add(new Action(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        return result;
    }

    internal static void AddAction(long guideId, string stepId,
        string variableId, string kind, string? value, string? sourceVariableId,
        string? targetUsageId, string? property, string? timing)
    {
        if (kind is not ("SetValue" or "Clear" or "CopyFromVariable" or "ReadFromTarget"))
            throw new ArgumentException("סוג הפעולה אינו נתמך.");
        if (kind == "SetValue" && (value is null || value.Length > 1000))
            throw new ArgumentException("יש להזין ערך באורך עד 1000 תווים.");
        if (kind == "CopyFromVariable" && string.IsNullOrWhiteSpace(sourceVariableId))
            throw new ArgumentException("יש לבחור משתנה מקור.");
        if (kind == "ReadFromTarget" && (string.IsNullOrWhiteSpace(targetUsageId) ||
            property is not ("text" or "value" or "name" or "automation-id" or "frame-url-fragment")))
            throw new ArgumentException("מקור או מאפיין לכידה אינו תקין.");
        if (!Enum.TryParse<GuideMe.Core.Guides.StepCaptureTiming>(timing ?? "AfterAction", out var captureTiming))
            throw new ArgumentException("מועד הלכידה אינו תקין.");
        if (captureTiming == GuideMe.Core.Guides.StepCaptureTiming.DuringStep &&
            kind != "ReadFromTarget")
            throw new ArgumentException("קליטה במהלך השלב מיועדת לקריאה מרכיב בלבד.");
        using var connection = Open();
        EnsureSchema(connection);
        using var cmd = connection.CreateCommand();
        if (kind == "ReadFromTarget")
        {
            cmd.CommandText = @"SELECT c.Runtime FROM StepTargets t
                JOIN Steps s ON s.GuideId=t.GuideId AND s.StepId=t.StepId
                JOIN ApplicationContexts c ON c.GuideId=s.GuideId AND c.ContextId=s.ApplicationContextId
                WHERE t.GuideId=$checkGuide AND t.StepId=$checkStep AND t.UsageId=$checkUsage";
            cmd.Parameters.AddWithValue("$checkGuide", guideId);
            cmd.Parameters.AddWithValue("$checkStep", stepId);
            cmd.Parameters.AddWithValue("$checkUsage", targetUsageId!);
            var runtime = cmd.ExecuteScalar() as string;
            if (runtime is null)
                throw new InvalidOperationException("הרכיב שנבחר אינו שייך לשלב.");
            if (runtime == "Web" && property is not ("text" or "value" or "frame-url-fragment")
                || runtime == "Windows" && property is not ("name" or "automation-id"))
                throw new ArgumentException("המאפיין אינו נתמך בסוג אפליקציית היעד.");
            cmd.Parameters.Clear();
        }
        cmd.CommandText = @"INSERT INTO StepVariableActions
            (GuideId,StepId,ActionId,ActionOrder,VariableId,ActionKind,
             LiteralValue,SourceVariableId,TargetUsageId,Property,Timing)
            VALUES ($guide,$step,$id,
             (SELECT COALESCE(MAX(ActionOrder),-1)+1 FROM StepVariableActions
              WHERE GuideId=$guide AND StepId=$step),
             $variable,$kind,$value,$source,$usage,$property,$timing)";
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        cmd.Parameters.AddWithValue("$id", "action-" + Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$variable", variableId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$value", kind == "SetValue" ? (object)value! : DBNull.Value);
        cmd.Parameters.AddWithValue("$source", kind == "CopyFromVariable"
            ? (object)sourceVariableId! : DBNull.Value);
        cmd.Parameters.AddWithValue("$usage", kind == "ReadFromTarget"
            ? (object)targetUsageId! : DBNull.Value);
        cmd.Parameters.AddWithValue("$property", kind == "ReadFromTarget"
            ? (object)property! : DBNull.Value);
        cmd.Parameters.AddWithValue("$timing", captureTiming.ToString());
        try { cmd.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        { throw new InvalidOperationException("לא ניתן לשמור פעולה: בדוק את המשתנה ואת השלב.", ex); }
    }

    /// <summary>Persist a picked, independent source target and its read action
    /// in one transaction; neither the primary target nor completion rules change.</summary>
    internal static void AddPickedRead(long guideId, string stepId, string variableId,
        string runtime, IReadOnlyList<V5GuideWriter.TargetLocator> locators,
        IReadOnlyList<V5GuideWriter.TargetLocator> frames, string? displayName,
        string property, string timing)
    {
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
        if (!Enum.TryParse<GuideMe.Core.Guides.StepCaptureTiming>(timing, false, out _))
            throw new ArgumentException("מועד הקריאה אינו תקין.");
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
             TargetUsageId,Property,Timing)
            VALUES ($guide,$step,$action,
                (SELECT COALESCE(MAX(ActionOrder),-1)+1 FROM StepVariableActions
                 WHERE GuideId=$guide AND StepId=$step),
                $variable,'ReadFromTarget',$usage,$property,$timing)";
        cmd.Parameters.AddWithValue("$action", "action-" + Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$property", property);
        cmd.Parameters.AddWithValue("$timing", timing);
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    internal static void MoveAction(long guideId, string stepId, string actionId, int direction)
    {
        if (direction is not (-1 or 1))
            throw new ArgumentException("כיוון שינוי סדר אינו תקין.");
        using var connection = Open();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT ActionId,ActionOrder FROM StepVariableActions
            WHERE GuideId=$guide AND StepId=$step ORDER BY ActionOrder";
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        var rows = new List<(string Id,int Order)>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) rows.Add((reader.GetString(0),reader.GetInt32(1)));
        var index = rows.FindIndex(x => x.Id == actionId);
        if (index < 0) throw new InvalidOperationException("פעולת המשתנה אינה קיימת.");
        var destination = index + direction;
        if (destination < 0 || destination >= rows.Count) return;
        (rows[index],rows[destination])=(rows[destination],rows[index]);
        // Negative temporary orders avoid colliding with the unique
        // (GuideId,StepId,ActionOrder) constraint during re-numbering.
        for (var i=0;i<rows.Count;i++)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = @"UPDATE StepVariableActions SET ActionOrder=$order
                WHERE GuideId=$guide AND StepId=$step AND ActionId=$id";
            cmd.Parameters.AddWithValue("$guide",guideId);
            cmd.Parameters.AddWithValue("$step",stepId);
            cmd.Parameters.AddWithValue("$id",rows[i].Id);
            cmd.Parameters.AddWithValue("$order",-i-1000000);
            cmd.ExecuteNonQuery();
        }
        for (var i=0;i<rows.Count;i++)
        {
            cmd.Parameters["$id"].Value=rows[i].Id;
            cmd.Parameters["$order"].Value=i;
            cmd.ExecuteNonQuery();
        }
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

    /// <summary>
    /// Bind one persisted primary locator to a guide variable. The picker owns
    /// target discovery; the editor selects only the variable (and CSS attribute).
    /// </summary>
    internal static void BindPrimaryTarget(long guideId, string stepId,
        string variableId, string? attribute)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        cmd.Parameters.AddWithValue("$variable", variableId);
        cmd.CommandText = @"SELECT COUNT(*) FROM GuideVariables
            WHERE GuideId=$guide AND VariableId=$variable";
        if (Convert.ToInt32(cmd.ExecuteScalar()) != 1)
            throw new ArgumentException("יש לבחור משתנה שהוגדר בלומדה.");

        cmd.CommandText = @"SELECT t.ElementId,e.Runtime,e.LocatorsJson
            FROM StepTargets t JOIN Elements e
              ON e.GuideId=t.GuideId AND e.ElementId=t.ElementId
            WHERE t.GuideId=$guide AND t.StepId=$step AND t.Purpose='Primary'";
        string elementId, runtime, json;
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read())
                throw new InvalidOperationException("יש לבחור רכיב במסך לפני קישור משתנה.");
            elementId = reader.GetString(0);
            runtime = reader.GetString(1);
            json = reader.GetString(2);
            if (reader.Read())
                throw new InvalidOperationException("נמצאו מספר רכיבים ראשיים.");
        }
        cmd.Parameters.AddWithValue("$element", elementId);
        cmd.CommandText = @"SELECT COUNT(*) FROM StepTargets
            WHERE GuideId=$guide AND ElementId=$element";
        if (Convert.ToInt32(cmd.ExecuteScalar()) != 1)
            throw new InvalidOperationException("הרכיב משותף לשלבים נוספים; יש לבחור רכיב ייעודי לשלב.");

        var locators = JsonSerializer.Deserialize<List<V5GuideWriter.TargetLocator>>(json)
            ?? throw new InvalidOperationException("לא ניתן לקרוא מזהה רכיב.");
        if (locators.Count != 1)
            throw new InvalidOperationException("קישור משתנה דורש מזהה רכיב ראשי יחיד.");
        var selected = locators[0];
        var token = "{{var:" + variableId + "}}";
        string updated;
        if (runtime == "Windows")
        {
            if (selected.Strategy is not ("name" or "automation-id"))
                throw new InvalidOperationException("סוג מזהה Windows אינו נתמך.");
            updated = token;
        }
        else if (runtime == "Web")
        {
            if (selected.Strategy != "css" || string.IsNullOrWhiteSpace(attribute)
                || !Regex.IsMatch(attribute, @"^[a-zA-Z_][\w-]*$"))
                throw new ArgumentException("יש לבחור מאפיין מתוך מזהה רכיב ה־Web.");
            var pattern = @"\[\s*" + Regex.Escape(attribute) +
                @"\s*=\s*(?<q>['""])(?<value>.*?)\k<q>\s*\]";
            var matches = Regex.Matches(selected.Value, pattern, RegexOptions.CultureInvariant);
            if (matches.Count != 1)
                throw new InvalidOperationException("לא נמצא ערך מאפיין חד־משמעי במזהה הרכיב.");
            var value = matches[0].Groups["value"];
            updated = selected.Value[..value.Index] + token
                + selected.Value[(value.Index + value.Length)..];
        }
        else throw new InvalidOperationException("סוג אפליקציית יעד אינו נתמך.");

        cmd.CommandText = @"UPDATE Elements SET LocatorsJson=$locators
            WHERE GuideId=$guide AND ElementId=$element";
        cmd.Parameters.AddWithValue("$locators", JsonSerializer.Serialize(
            new[] { selected with { Value = updated } }));
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("שמירת קישור המשתנה נכשלה.");
        tx.Commit();
    }

}
