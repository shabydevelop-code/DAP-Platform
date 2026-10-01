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
        CancellationToken cancellationToken,
        int? startStepOrder = null)
    {
        var frameUrlFragments = new Dictionary<string, string>(StringComparer.Ordinal);
        var captureSourceStepIds = guideSteps
            .SelectMany(ReferencedRuntimeValueSteps)
            .ToHashSet(StringComparer.Ordinal);
        string? previousStepFragment = null;

        var orderedSteps = guideSteps.OrderBy(step => step.Order).ToArray();
        var startIndex = 0;
        if (startStepOrder is not null)
        {
            startIndex = Array.FindIndex(orderedSteps, step => step.Order == startStepOrder.Value);
            if (startIndex < 0)
                throw new InvalidOperationException(
                    $"Guide does not contain Step order {startStepOrder.Value}.");
        }

        for (var stepIndex = startIndex; stepIndex < orderedSteps.Length; stepIndex++)
        {
            var persistedStep = orderedSteps[stepIndex];
            cancellationToken.ThrowIfCancellationRequested();

            if (persistedStep.Target?.Runtime != TargetRuntime.Web)
                throw new InvalidOperationException(
                    $"Guide Step '{persistedStep.Id}' is not a Web Step and cannot run in WebGuideRuntime.");

            var step = MaterializeRuntimeValues(persistedStep, frameUrlFragments);
            var frame = await ResolveTargetFrameAsync(page, step.Target!.FrameContext, cancellationToken);
            if (frame is not null)
            {
                var fragment = new Uri(frame.Url).Fragment;

                if (captureSourceStepIds.Contains(step.Id) && previousStepFragment is not null)
                {
                    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                    while (string.Equals(fragment, previousStepFragment, StringComparison.Ordinal)
                           && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(50, cancellationToken);
                        frame = await ResolveTargetFrameAsync(page, step.Target.FrameContext, cancellationToken);
                        if (frame is null)
                            continue;
                        fragment = new Uri(frame.Url).Fragment;
                    }

                    if (string.Equals(fragment, previousStepFragment, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Guide Step '{step.Id}' is a runtime URL capture source, but its target frame did not leave the previous Step URL.");
                }

                frameUrlFragments[step.Id] = fragment;
                previousStepFragment = fragment;
            }

            Console.Error.WriteLine($"[DAP guide] starting Step {stepIndex + 1}/{orderedSteps.Length} '{step.Id}'.");
            await _steps.RunActiveStepAsync(page, step, stepIndex + 1, orderedSteps.Length, cancellationToken);
            Console.Error.WriteLine($"[DAP guide] completed Step {stepIndex + 1}/{orderedSteps.Length} '{step.Id}'.");
        }
    }

    private static IEnumerable<string> ReferencedRuntimeValueSteps(GuideStep step)
    {
        if (step.Target is null)
            yield break;

        foreach (Match match in RuntimeValueToken.Matches(step.Target.Locator.Value))
            yield return match.Groups["step"].Value;

        foreach (var anchor in step.Target.Anchors)
            foreach (Match match in RuntimeValueToken.Matches(anchor.Locator.Value))
                yield return match.Groups["step"].Value;
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
