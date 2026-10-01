namespace DAP.Data.Sqlite;

public sealed record SqliteDatabaseOptions(string DatabasePath)
{
    public const string DatabasePathEnvironmentVariable = "DAP_DATABASE_PATH";

    public static SqliteDatabaseOptions CreateDefault()
    {
        var overridePath = Environment.GetEnvironmentVariable(DatabasePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return new SqliteDatabaseOptions(Path.GetFullPath(overridePath));

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            throw new InvalidOperationException("Windows ProgramData path could not be resolved.");

        return new SqliteDatabaseOptions(
            Path.Combine(programData, "DAP", "Data", "DAP.db"));
    }
}
