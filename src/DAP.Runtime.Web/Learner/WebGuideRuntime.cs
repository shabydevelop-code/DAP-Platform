using System.Text.RegularExpressions;
using DAP.Core.Guides;
using DAP.Core.Targets;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Learner;

public sealed class WebGuideRuntime
{
    private static readonly Regex RuntimeValueToken = new(
        @"\{\{step:(?<step>[^}:]+):frame-url-fragment\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly WebLearnerRuntime _steps;

    public WebGuideRuntime(WebLearnerRuntime steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    }

    public async Task RunAsync(
        IPage page,
        IReadOnlyList<GuideStep> guideSteps,
        CancellationToken cancellationToken)
    {
        var frameUrlFragments = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var persistedStep in guideSteps.OrderBy(step => step.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (persistedStep.Target?.Runtime != TargetRuntime.Web)
                throw new InvalidOperationException(
                    $"Guide Step '{persistedStep.Id}' is not a Web Step and cannot run in WebGuideRuntime.");

            var step = MaterializeRuntimeValues(persistedStep, frameUrlFragments);
            var frame = await ResolveTargetFrameAsync(page, step.Target!.FrameContext, cancellationToken);
            if (frame is not null)
                frameUrlFragments[step.Id] = new Uri(frame.Url).Fragment;

            await _steps.RunActiveStepAsync(page, step, cancellationToken);
        }
    }

    private static GuideStep MaterializeRuntimeValues(
        GuideStep step,
        IReadOnlyDictionary<string, string> frameUrlFragments)
    {
        if (step.Target is null)
            return step;

        Locator MaterializeLocator(Locator locator) =>
            locator with { Value = Materialize(locator.Value, step.Id, frameUrlFragments) };

        var target = step.Target with
        {
            Locator = MaterializeLocator(step.Target.Locator),
            Anchors = step.Target.Anchors
                .Select(anchor => anchor with { Locator = MaterializeLocator(anchor.Locator) })
                .ToArray()
        };

        return step with { Target = target };
    }

    private static string Materialize(
        string value,
        string activeStepId,
        IReadOnlyDictionary<string, string> frameUrlFragments) =>
        RuntimeValueToken.Replace(value, match =>
        {
            var sourceStepId = match.Groups["step"].Value;
            if (!frameUrlFragments.TryGetValue(sourceStepId, out var fragment))
                throw new InvalidOperationException(
                    $"Guide Step '{activeStepId}' references runtime URL fragment from Step '{sourceStepId}', but that Step has not captured one.");

            // Runtime values are substituted into persisted locator text. Escape
            // characters that can terminate a single-quoted CSS attribute value.
            return fragment.Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("'", @"\'", StringComparison.Ordinal);
        });

    private static async Task<IFrame?> ResolveTargetFrameAsync(
        IPage page,
        FrameContext? frameContext,
        CancellationToken cancellationToken)
    {
        if (frameContext is null || frameContext.Path.Count == 0)
            return page.MainFrame;

        IFrame current = page.MainFrame;
        foreach (var frameLocator in frameContext.Path)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var locator = frameLocator.Strategy.Trim().ToLowerInvariant() switch
            {
                "css" => current.Locator(frameLocator.Value),
                "text" => current.GetByText(frameLocator.Value),
                "label" => current.GetByLabel(frameLocator.Value),
                "role" when Enum.TryParse<AriaRole>(frameLocator.Value, true, out var role) => current.GetByRole(role),
                _ => throw new NotSupportedException(
                    $"Unsupported Web frame locator strategy '{frameLocator.Strategy}'.")
            };

            if (await locator.CountAsync() != 1)
                return null;

            var handle = await locator.ElementHandleAsync();
            var next = handle is null ? null : await handle.ContentFrameAsync();
            if (next is null || next.IsDetached)
                return null;

            current = next;
        }

        return current;
    }
}
