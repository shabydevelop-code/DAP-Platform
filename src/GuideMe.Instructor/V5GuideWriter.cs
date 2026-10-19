using Microsoft.Data.Sqlite;
using System;
using System.IO;

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
}
