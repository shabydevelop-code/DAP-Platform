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
        command.CommandText = @"DELETE FROM GuideVariables WHERE GuideId=$guide AND VariableId=$id";
        command.Parameters.AddWithValue("$guide", guideId);
        command.Parameters.AddWithValue("$id", variableId);
        try { if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("המשתנה לא נמצא."); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        { throw new InvalidOperationException("לא ניתן למחוק משתנה שמשמש בשלבי הלומדה.", e); }
    }
    internal sealed record Action(string Id, int Order, string VariableId, string Kind,
        string? LiteralValue, string? SourceVariableId, string Timing);

    internal static IReadOnlyList<Action> ListActions(long guideId, string stepId)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT ActionId,ActionOrder,VariableId,ActionKind,LiteralValue,
            SourceVariableId,Timing FROM StepVariableActions
            WHERE GuideId=$guide AND StepId=$step ORDER BY ActionOrder";
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        using var reader = cmd.ExecuteReader();
        var result = new List<Action>();
        while (reader.Read())
            result.Add(new Action(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6)));
        return result;
    }

    internal static void AddAction(long guideId, string stepId,
        string variableId, string kind, string? value, string? sourceVariableId)
    {
        if (kind is not ("SetValue" or "Clear" or "CopyFromVariable"))
            throw new ArgumentException("סוג הפעולה אינו נתמך.");
        if (kind == "SetValue" && (value is null || value.Length > 1000))
            throw new ArgumentException("יש להזין ערך באורך עד 1000 תווים.");
        if (kind == "CopyFromVariable" && string.IsNullOrWhiteSpace(sourceVariableId))
            throw new ArgumentException("יש לבחור משתנה מקור.");
        using var connection = Open();
        EnsureSchema(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO StepVariableActions
            (GuideId,StepId,ActionId,ActionOrder,VariableId,ActionKind,
             LiteralValue,SourceVariableId,Timing)
            VALUES ($guide,$step,$id,
             (SELECT COALESCE(MAX(ActionOrder),-1)+1 FROM StepVariableActions
              WHERE GuideId=$guide AND StepId=$step),
             $variable,$kind,$value,$source,'AfterAction')";
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        cmd.Parameters.AddWithValue("$id", "action-" + Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$variable", variableId);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$value", kind == "SetValue" ? (object)value! : DBNull.Value);
        cmd.Parameters.AddWithValue("$source", kind == "CopyFromVariable"
            ? (object)sourceVariableId! : DBNull.Value);
        try { cmd.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        { throw new InvalidOperationException("לא ניתן לשמור פעולה: בדוק את המשתנה ואת השלב.", ex); }
    }

    internal static void DeleteAction(long guideId, string stepId, string actionId)
    {
        using var connection = Open();
        EnsureSchema(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"DELETE FROM StepVariableActions
            WHERE GuideId=$guide AND StepId=$step AND ActionId=$id";
        cmd.Parameters.AddWithValue("$guide", guideId);
        cmd.Parameters.AddWithValue("$step", stepId);
        cmd.Parameters.AddWithValue("$id", actionId);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("פעולת המשתנה לא נמצאה.");
    }

}
