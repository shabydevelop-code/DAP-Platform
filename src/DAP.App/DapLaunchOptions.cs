namespace DAP.App;

public enum DapLaunchMode
{
    InfrastructureCheck,
    LearnerWeb
}

public sealed record DapLaunchOptions(DapLaunchMode Mode, string? GuideId)
{
    public static DapLaunchOptions? Parse(string[] args)
    {
        if (args.Length == 1 && args[0] == "--check")
            return new(DapLaunchMode.InfrastructureCheck, null);

        if (args.Length == 2 && args[0] == "--learner-web" && !string.IsNullOrWhiteSpace(args[1]))
            return new(DapLaunchMode.LearnerWeb, args[1]);

        Console.Error.WriteLine("Usage: DAP.exe --check | --learner-web <guide-id>");
        return null;
    }
}
