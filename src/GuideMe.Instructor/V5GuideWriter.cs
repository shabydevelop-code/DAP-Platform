using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace GuideMe.Instructor;

// Explicitly scoped writer: creates only a Guides row; no schema changes or step data.
internal static class V5GuideWriter
{
    internal static V5GuideReader.GuideInfo CreateGuide(string key, string name)
    {
        key = key.Trim();
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("יש להזין שם ומפתח לומדה.");
        if (key.Length > 128 || name.Length > 256)
            throw new ArgumentException("שם הלומדה או המפתח ארוכים מדי.");
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
