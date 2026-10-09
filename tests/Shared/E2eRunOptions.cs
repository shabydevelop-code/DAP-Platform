namespace DAP.Testing;

/// <summary>Common command-line contract for Web and Windows E2E runners.</summary>
internal sealed record E2eRunOptions(string? GuideId, string? PublishedDapDirectory, bool Manual, bool Hybrid, bool ResetGuide)
{
    public static E2eRunOptions Parse(string[] args, string platform)
    {
        string? guideId = null;
        string? publishedDapDirectory = null;
        var manual = false;
        var hybrid = false;
        var reset = false;
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (argument.Equals("--guide", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("--published-dap", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    throw new ArgumentException(argument.Equals("--guide", StringComparison.OrdinalIgnoreCase)
                        ? "--guide requires a persisted Guide ID."
                        : "--published-dap requires a directory containing DAP.exe.");
                var value = args[++i];
                if (argument.Equals("--guide", StringComparison.OrdinalIgnoreCase))
                    guideId = value;
                else
                    publishedDapDirectory = Path.GetFullPath(value);
            }
            else if (argument.Equals("--manual", StringComparison.OrdinalIgnoreCase)) manual = true;
            else if (argument.Equals("--hybrid", StringComparison.OrdinalIgnoreCase)) hybrid = true;
            else if (argument.Equals("--reset-guide", StringComparison.OrdinalIgnoreCase)) reset = true;
            else throw new ArgumentException($"Unsupported {platform} E2E argument '{argument}'.");
        }

        if (!reset)
        {
            if (string.IsNullOrWhiteSpace(guideId))
                throw new ArgumentException("--guide <GuideId> is required.");
            if (manual == hybrid)
                throw new ArgumentException($"Choose exactly one {platform} run mode: --manual or --hybrid.");
        }
        return new E2eRunOptions(guideId, publishedDapDirectory, manual, hybrid, reset);
    }
}
