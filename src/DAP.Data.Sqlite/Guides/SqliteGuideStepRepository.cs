using System.Text.Json;
using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Data.Guides;
using Microsoft.Data.Sqlite;

namespace DAP.Data.Sqlite.Guides;

public sealed class SqliteGuideStepRepository : IGuideStepRepository
{
    private readonly SqliteConnectionFactory _connections;

    public SqliteGuideStepRepository(SqliteConnectionFactory connections)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async Task<IReadOnlyList<GuideStep>> GetStepsAsync(string guideId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var rows = new List<StepRow>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
SELECT s.Id, s.Key, s.StepOrder, s.AdvanceMode, s.Runtime, s.LocatorStrategy, s.LocatorValue, s.FrameContextJson,
       s.ContextKind, s.ContextValue, s.BubbleContent, s.BubblePlacement, s.ValidationKind, s.ValidationExpectedValue, s.ValidationOptionsJson, s.IsEnabled, s.AutomationValue, s.ApplicationContextKey
FROM GuideSteps s
JOIN Guides g ON g.Id = s.GuideId
WHERE g.Key = $guideKey
ORDER BY s.StepOrder;
""";
            command.Parameters.AddWithValue("$guideKey", guideId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(ReadStep(reader));
        }

        var result = new List<GuideStep>(rows.Count);
        foreach (var row in rows)
            result.Add(await MaterializeAsync(connection, row, cancellationToken));
        return result;
    }

    public async Task<IReadOnlyList<GuideApplicationContext>> GetApplicationContextsAsync(string guideId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenAsync(cancellationToken);
        var contexts = new List<GuideApplicationContext>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
SELECT c.Id, c.Key, c.Runtime
FROM GuideApplicationContexts c
JOIN Guides g ON g.Id = c.GuideId
WHERE g.Key = $guideKey
ORDER BY c.Id;
""";
        command.Parameters.AddWithValue("$guideKey", guideId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(long Id, string Key, TargetRuntime Runtime)>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetInt64(0), reader.GetString(1), Enum.Parse<TargetRuntime>(reader.GetString(2))));
        await reader.DisposeAsync();

        foreach (var row in rows)
        {
            var matchers = new List<ApplicationContextMatcher>();
            await using var matcherCommand = connection.CreateCommand();
            matcherCommand.CommandText = "SELECT MatcherOrder,Kind,Value FROM ApplicationContextMatchers WHERE ApplicationContextId=$id ORDER BY MatcherOrder;";
            matcherCommand.Parameters.AddWithValue("$id", row.Id);
            await using var matcherReader = await matcherCommand.ExecuteReaderAsync(cancellationToken);
            while (await matcherReader.ReadAsync(cancellationToken))
                matchers.Add(new ApplicationContextMatcher(matcherReader.GetInt32(0), matcherReader.GetString(1), matcherReader.GetString(2)));
            contexts.Add(new GuideApplicationContext(row.Key, row.Runtime, matchers));
        }
        return contexts;
    }

    public async Task ReplaceApplicationContextsAsync(string guideId, IReadOnlyList<GuideApplicationContext> contexts, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guideId);
        ArgumentNullException.ThrowIfNull(contexts);
        if (contexts.Select(context => context.Key).Distinct(StringComparer.Ordinal).Count() != contexts.Count)
            throw new ArgumentException("Application Context keys must be unique within a Guide.", nameof(contexts));

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long numericGuideId;
        await using (var guide = connection.CreateCommand())
        {
            guide.Transaction = (SqliteTransaction)transaction;
            guide.CommandText = "INSERT INTO Guides(Key,Name) VALUES($key,$key) ON CONFLICT(Key) DO NOTHING; SELECT Id FROM Guides WHERE Key=$key;";
            guide.Parameters.AddWithValue("$key", guideId);
            numericGuideId = Convert.ToInt64(await guide.ExecuteScalarAsync(cancellationToken));
        }
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM GuideApplicationContexts WHERE GuideId=$guideId;";
            Add(delete, "$guideId", numericGuideId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var context in contexts)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO GuideApplicationContexts(GuideId,Key,Runtime) VALUES($guideId,$key,$runtime); SELECT last_insert_rowid();";
            Add(insert, "$guideId", numericGuideId); Add(insert, "$key", context.Key); Add(insert, "$runtime", context.Runtime.ToString());
            var contextId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            foreach (var matcher in context.Matchers.OrderBy(matcher => matcher.Order))
            {
                await using var matcherInsert = connection.CreateCommand();
                matcherInsert.Transaction = (SqliteTransaction)transaction;
                matcherInsert.CommandText = "INSERT INTO ApplicationContextMatchers(ApplicationContextId,MatcherOrder,Kind,Value) VALUES($id,$order,$kind,$value);";
                Add(matcherInsert, "$id", contextId); Add(matcherInsert, "$order", matcher.Order); Add(matcherInsert, "$kind", matcher.Kind); Add(matcherInsert, "$value", matcher.Value);
                await matcherInsert.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveStepAsync(string guideId, GuideStep step, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guideId);
        ArgumentNullException.ThrowIfNull(step);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long numericGuideId;
        await using (var guide = connection.CreateCommand())
        {
            guide.Transaction = (SqliteTransaction)transaction;
            guide.CommandText = """
INSERT INTO Guides(Key, Name) VALUES($key, $key)
ON CONFLICT(Key) DO NOTHING;
SELECT Id FROM Guides WHERE Key = $key;
""";
            guide.Parameters.AddWithValue("$key", guideId);
            numericGuideId = Convert.ToInt64(await guide.ExecuteScalarAsync(cancellationToken));
        }

        long numericStepId;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
INSERT INTO GuideSteps(
 GuideId,Key,StepOrder,AdvanceMode,Runtime,LocatorStrategy,LocatorValue,FrameContextJson,ContextKind,ContextValue,
 BubbleContent,BubblePlacement,ValidationKind,ValidationExpectedValue,ValidationOptionsJson,IsEnabled,AutomationValue,ApplicationContextKey)
VALUES($guideId,$key,$order,$advance,$runtime,$strategy,$value,$frame,$contextKind,$contextValue,$content,$placement,$validation,$expected,$options,$enabled,$automationValue,$applicationContextKey)
ON CONFLICT(GuideId,Key) DO UPDATE SET
 StepOrder=excluded.StepOrder, AdvanceMode=excluded.AdvanceMode,
 Runtime=excluded.Runtime, LocatorStrategy=excluded.LocatorStrategy, LocatorValue=excluded.LocatorValue,
 FrameContextJson=excluded.FrameContextJson, ContextKind=excluded.ContextKind, ContextValue=excluded.ContextValue,
 BubbleContent=excluded.BubbleContent, BubblePlacement=excluded.BubblePlacement,
 ValidationKind=excluded.ValidationKind, ValidationExpectedValue=excluded.ValidationExpectedValue,
 ValidationOptionsJson=excluded.ValidationOptionsJson, IsEnabled=excluded.IsEnabled, AutomationValue=excluded.AutomationValue,
 ApplicationContextKey=excluded.ApplicationContextKey;
SELECT Id FROM GuideSteps WHERE GuideId=$guideId AND Key=$key;
""";
            Add(command,"$guideId",numericGuideId); Add(command,"$key",step.Id); Add(command,"$order",step.Order);
            Add(command,"$advance",step.AdvanceMode.ToString()); Add(command,"$runtime",step.Target?.Runtime.ToString());
            Add(command,"$strategy",step.Target?.Locator.Strategy); Add(command,"$value",step.Target?.Locator.Value);
            Add(command,"$frame",step.Target?.FrameContext is null ? null : JsonSerializer.Serialize(step.Target.FrameContext.Path));
            Add(command,"$contextKind",step.Context?.Kind); Add(command,"$contextValue",step.Context?.Value);
            Add(command,"$content",step.Bubble.Content); Add(command,"$placement",step.Bubble.Placement.ToString());
            Add(command,"$validation",step.Validation?.Kind); Add(command,"$expected",step.Validation?.ExpectedValue);
            Add(command,"$options",step.Validation?.Options is null ? null : JsonSerializer.Serialize(step.Validation.Options));
            Add(command,"$enabled",step.IsEnabled ? 1 : 0); Add(command,"$automationValue",step.AutomationValue); Add(command,"$applicationContextKey",step.ApplicationContextKey);
            numericStepId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM TargetAnchors WHERE GuideStepId=$id;";
            Add(delete,"$id",numericStepId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        if(step.Target is not null)
        {
            for(var i=0;i<step.Target.Anchors.Count;i++)
            {
                var anchor=step.Target.Anchors[i];
                await using var insert=connection.CreateCommand();
                insert.Transaction=(SqliteTransaction)transaction;
                insert.CommandText="INSERT INTO TargetAnchors(GuideStepId,AnchorOrder,Relation,LocatorStrategy,LocatorValue) VALUES($id,$order,$relation,$strategy,$value);";
                Add(insert,"$id",numericStepId); Add(insert,"$order",i); Add(insert,"$relation",anchor.Relation.ToString());
                Add(insert,"$strategy",anchor.Locator.Strategy); Add(insert,"$value",anchor.Locator.Value);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (var deleteCapture = connection.CreateCommand())
        {
            deleteCapture.Transaction = (SqliteTransaction)transaction;
            deleteCapture.CommandText = "DELETE FROM StepCaptures WHERE GuideStepId=$id;";
            Add(deleteCapture,"$id",numericStepId);
            await deleteCapture.ExecuteNonQueryAsync(cancellationToken);
        }

        if (step.Capture is not null)
        {
            await using var insertCapture = connection.CreateCommand();
            insertCapture.Transaction = (SqliteTransaction)transaction;
            insertCapture.CommandText = "INSERT INTO StepCaptures(GuideStepId,Runtime,LocatorStrategy,LocatorValue,Property,Pattern) VALUES($id,$runtime,$strategy,$value,$property,$pattern);";
            Add(insertCapture,"$id",numericStepId);
            Add(insertCapture,"$runtime",step.Capture.Runtime.ToString());
            Add(insertCapture,"$strategy",step.Capture.Locator.Strategy);
            Add(insertCapture,"$value",step.Capture.Locator.Value);
            Add(insertCapture,"$property",step.Capture.Property);
            Add(insertCapture,"$pattern",step.Capture.Pattern);
            await insertCapture.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCompletion = connection.CreateCommand())
        {
            deleteCompletion.Transaction = (SqliteTransaction)transaction;
            deleteCompletion.CommandText = "DELETE FROM StepCompletionConditions WHERE GuideStepId=$id;";
            Add(deleteCompletion,"$id",numericStepId);
            await deleteCompletion.ExecuteNonQueryAsync(cancellationToken);
        }

        if (step.CompletionConditions is not null)
        {
            for (var i = 0; i < step.CompletionConditions.Count; i++)
            {
                var condition = step.CompletionConditions[i];
                await using var insertCompletion = connection.CreateCommand();
                insertCompletion.Transaction = (SqliteTransaction)transaction;
                insertCompletion.CommandText = "INSERT INTO StepCompletionConditions(GuideStepId,ConditionOrder,Kind,ExpectedValue,TargetJson) VALUES($id,$order,$kind,$expected,$target);";
                Add(insertCompletion,"$id",numericStepId);
                Add(insertCompletion,"$order",i);
                Add(insertCompletion,"$kind",condition.Kind);
                Add(insertCompletion,"$expected",condition.ExpectedValue);
                Add(insertCompletion,"$target",JsonSerializer.Serialize(condition.Target));
                await insertCompletion.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }


    public async Task ReplaceStepsAsync(
        string guideId,
        IReadOnlyList<GuideStep> steps,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guideId);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Select(step => step.Id).Distinct(StringComparer.Ordinal).Count() != steps.Count)
            throw new ArgumentException("Replacement Guide Steps must have unique textual IDs.", nameof(steps));
        if (steps.Select(step => step.Order).Distinct().Count() != steps.Count)
            throw new ArgumentException("Replacement Guide Steps must have unique Step orders.", nameof(steps));

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long numericGuideId;
        await using (var guide = connection.CreateCommand())
        {
            guide.Transaction = (SqliteTransaction)transaction;
            guide.CommandText = """
INSERT INTO Guides(Key, Name) VALUES($key, $key)
ON CONFLICT(Key) DO NOTHING;
SELECT Id FROM Guides WHERE Key = $key;
""";
            guide.Parameters.AddWithValue("$key", guideId);
            numericGuideId = Convert.ToInt64(await guide.ExecuteScalarAsync(cancellationToken));
        }

        await using (var deleteExisting = connection.CreateCommand())
        {
            deleteExisting.Transaction = (SqliteTransaction)transaction;
            deleteExisting.CommandText = "DELETE FROM GuideSteps WHERE GuideId=$guideId;";
            Add(deleteExisting, "$guideId", numericGuideId);
            await deleteExisting.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var step in steps.OrderBy(step => step.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            long numericStepId;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
INSERT INTO GuideSteps(
 GuideId,Key,StepOrder,AdvanceMode,Runtime,LocatorStrategy,LocatorValue,FrameContextJson,ContextKind,ContextValue,
 BubbleContent,BubblePlacement,ValidationKind,ValidationExpectedValue,ValidationOptionsJson,IsEnabled,AutomationValue)
VALUES($guideId,$key,$order,$advance,$runtime,$strategy,$value,$frame,$contextKind,$contextValue,$content,$placement,$validation,$expected,$options,$enabled,$automationValue);
SELECT last_insert_rowid();
""";
                Add(command,"$guideId",numericGuideId); Add(command,"$key",step.Id); Add(command,"$order",step.Order);
                Add(command,"$advance",step.AdvanceMode.ToString()); Add(command,"$runtime",step.Target?.Runtime.ToString());
                Add(command,"$strategy",step.Target?.Locator.Strategy); Add(command,"$value",step.Target?.Locator.Value);
                Add(command,"$frame",step.Target?.FrameContext is null ? null : JsonSerializer.Serialize(step.Target.FrameContext.Path));
                Add(command,"$contextKind",step.Context?.Kind); Add(command,"$contextValue",step.Context?.Value);
                Add(command,"$content",step.Bubble.Content); Add(command,"$placement",step.Bubble.Placement.ToString());
                Add(command,"$validation",step.Validation?.Kind); Add(command,"$expected",step.Validation?.ExpectedValue);
                Add(command,"$options",step.Validation?.Options is null ? null : JsonSerializer.Serialize(step.Validation.Options));
            Add(command,"$enabled",step.IsEnabled ? 1 : 0); Add(command,"$automationValue",step.AutomationValue); Add(command,"$applicationContextKey",step.ApplicationContextKey);
                numericStepId = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            }

            if (step.Target is not null)
            {
                for (var i = 0; i < step.Target.Anchors.Count; i++)
                {
                    var anchor = step.Target.Anchors[i];
                    await using var insertAnchor = connection.CreateCommand();
                    insertAnchor.Transaction = (SqliteTransaction)transaction;
                    insertAnchor.CommandText = "INSERT INTO TargetAnchors(GuideStepId,AnchorOrder,Relation,LocatorStrategy,LocatorValue) VALUES($id,$order,$relation,$strategy,$value);";
                    Add(insertAnchor,"$id",numericStepId); Add(insertAnchor,"$order",i);
                    Add(insertAnchor,"$relation",anchor.Relation.ToString());
                    Add(insertAnchor,"$strategy",anchor.Locator.Strategy); Add(insertAnchor,"$value",anchor.Locator.Value);
                    await insertAnchor.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            if (step.Capture is not null)
            {
                await using var insertCapture = connection.CreateCommand();
                insertCapture.Transaction = (SqliteTransaction)transaction;
                insertCapture.CommandText = "INSERT INTO StepCaptures(GuideStepId,Runtime,LocatorStrategy,LocatorValue,Property,Pattern) VALUES($id,$runtime,$strategy,$value,$property,$pattern);";
                Add(insertCapture,"$id",numericStepId);
                Add(insertCapture,"$runtime",step.Capture.Runtime.ToString());
                Add(insertCapture,"$strategy",step.Capture.Locator.Strategy);
                Add(insertCapture,"$value",step.Capture.Locator.Value);
                Add(insertCapture,"$property",step.Capture.Property);
                Add(insertCapture,"$pattern",step.Capture.Pattern);
                await insertCapture.ExecuteNonQueryAsync(cancellationToken);
            }

            if (step.CompletionConditions is not null)
            {
                for (var i = 0; i < step.CompletionConditions.Count; i++)
                {
                    var condition = step.CompletionConditions[i];
                    await using var insertCompletion = connection.CreateCommand();
                    insertCompletion.Transaction = (SqliteTransaction)transaction;
                    insertCompletion.CommandText = "INSERT INTO StepCompletionConditions(GuideStepId,ConditionOrder,Kind,ExpectedValue,TargetJson) VALUES($id,$order,$kind,$expected,$target);";
                    Add(insertCompletion,"$id",numericStepId);
                    Add(insertCompletion,"$order",i);
                    Add(insertCompletion,"$kind",condition.Kind);
                    Add(insertCompletion,"$expected",condition.ExpectedValue);
                    Add(insertCompletion,"$target",JsonSerializer.Serialize(condition.Target));
                    await insertCompletion.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RenameGuideAsync(
        string currentKey,
        string newKey,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(newKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        await using var connection = await _connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
UPDATE Guides
SET Key = $newKey, Name = $newName
WHERE Key = $currentKey
  AND NOT EXISTS (SELECT 1 FROM Guides WHERE Key = $newKey);

UPDATE Guides
SET Name = $newName
WHERE Key = $newKey;
""";
        command.Parameters.AddWithValue("$currentKey", currentKey);
        command.Parameters.AddWithValue("$newKey", newKey);
        command.Parameters.AddWithValue("$newName", newName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Add(SqliteCommand c,string name,object? value)=>c.Parameters.AddWithValue(name,value??DBNull.Value);

    private static StepRow ReadStep(SqliteDataReader r)=>new(
        r.GetInt64(0),r.GetString(1),r.GetInt32(2),r.GetString(3),N(r,4),N(r,5),N(r,6),N(r,7),N(r,8),N(r,9),
        r.GetString(10),r.GetString(11),N(r,12),N(r,13),N(r,14),r.GetInt32(15) != 0,N(r,16),N(r,17));

    private static string? N(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);

    private static async Task<GuideStep> MaterializeAsync(SqliteConnection connection,StepRow row,CancellationToken ct)
    {
        TargetDescriptor? target=null;
        if(row.Runtime is not null && row.Strategy is not null && row.Value is not null)
        {
            var anchors=new List<Anchor>();
            await using var command=connection.CreateCommand();
            command.CommandText="SELECT Relation,LocatorStrategy,LocatorValue FROM TargetAnchors WHERE GuideStepId=$id ORDER BY AnchorOrder;";
            command.Parameters.AddWithValue("$id",row.NumericId);
            await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct))
                anchors.Add(new Anchor(new Locator(reader.GetString(1),reader.GetString(2)),Enum.Parse<AnchorRelation>(reader.GetString(0))));
            var framePath=row.FrameJson is null?null:JsonSerializer.Deserialize<Locator[]>(row.FrameJson);
            target=new TargetDescriptor(Enum.Parse<TargetRuntime>(row.Runtime),new Locator(row.Strategy,row.Value),anchors,framePath is null?null:new FrameContext(framePath));
        }

        var validation=row.ValidationKind is null?null:new ValidationDefinition(row.ValidationKind,row.Expected,row.OptionsJson is null?null:JsonSerializer.Deserialize<Dictionary<string,string>>(row.OptionsJson));
        var context=row.ContextKind is null || row.ContextValue is null ? null : new StepContextDefinition(row.ContextKind,row.ContextValue);
        StepCaptureDefinition? capture=null;
        await using (var captureCommand=connection.CreateCommand())
        {
            captureCommand.CommandText="SELECT Runtime,LocatorStrategy,LocatorValue,Property,Pattern FROM StepCaptures WHERE GuideStepId=$id;";
            captureCommand.Parameters.AddWithValue("$id",row.NumericId);
            await using var captureReader=await captureCommand.ExecuteReaderAsync(ct);
            if(await captureReader.ReadAsync(ct))
                capture=new StepCaptureDefinition(
                    Enum.Parse<TargetRuntime>(captureReader.GetString(0)),
                    new Locator(captureReader.GetString(1),captureReader.GetString(2)),
                    captureReader.GetString(3),
                    captureReader.IsDBNull(4)?null:captureReader.GetString(4));
        }
        var completionConditions = new List<StepCompletionCondition>();
        await using (var completionCommand = connection.CreateCommand())
        {
            completionCommand.CommandText = "SELECT Kind,ExpectedValue,TargetJson FROM StepCompletionConditions WHERE GuideStepId=$id ORDER BY ConditionOrder;";
            completionCommand.Parameters.AddWithValue("$id",row.NumericId);
            await using var completionReader = await completionCommand.ExecuteReaderAsync(ct);
            while (await completionReader.ReadAsync(ct))
            {
                var completionTarget = JsonSerializer.Deserialize<TargetDescriptor>(completionReader.GetString(2))
                    ?? throw new InvalidOperationException($"Could not deserialize completion target for Guide Step '{row.Key}'.");
                completionConditions.Add(new StepCompletionCondition(
                    completionReader.GetString(0),
                    completionTarget,
                    completionReader.IsDBNull(1) ? null : completionReader.GetString(1)));
            }
        }

        return new GuideStep(
            row.Key,row.Order,target,
            new BubbleDefinition(row.BubbleContent,Enum.Parse<BubblePlacement>(row.Placement)),
            validation,Enum.Parse<StepAdvanceMode>(row.AdvanceMode),context,capture,
            completionConditions.Count == 0 ? null : completionConditions,
            row.IsEnabled,
            row.AutomationValue,
            row.ApplicationContextKey);
    }

    private sealed record StepRow(
        long NumericId,string Key,int Order,string AdvanceMode,string? Runtime,string? Strategy,string? Value,string? FrameJson,
        string? ContextKind,string? ContextValue,string BubbleContent,string Placement,string? ValidationKind,string? Expected,string? OptionsJson,
        bool IsEnabled,string? AutomationValue,string? ApplicationContextKey);
}
