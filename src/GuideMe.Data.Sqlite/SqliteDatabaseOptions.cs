namespace GuideMe.Data.Sqlite;

public sealed record SqliteDatabaseOptions(string DatabasePath)
{
    public const string DatabasePathEnvironmentVariable = "GuideMe_DATABASE_PATH";

    // Separate default until explicitly approved migration of the active production DB.
    // GuideMe_DATABASE_PATH remains supported for controlled deployments.
    public static SqliteDatabaseOptions CreateV5Default()
    {
        var overridePath = Environment.GetEnvironmentVariable(DatabasePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
            return new SqliteDatabaseOptions(Path.GetFullPath(overridePath));

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
            throw new InvalidOperationException("Windows ProgramData path could not be resolved.");

        return new SqliteDatabaseOptions(
            Path.Combine(programData, "GuideMe", "Data", "GuideMe-V5.db"));
    }

}
