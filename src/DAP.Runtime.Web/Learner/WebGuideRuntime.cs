using System.Text.RegularExpressions;
using DAP.Core.Guides;
using DAP.Core.Targets;
using Microsoft.Playwright;

namespace DAP.Runtime.Web.Learner;

public sealed class WebGuideRuntime
{
    private static readonly Regex RuntimeValueToken = new(
        @"\{\{step:(?<step>[^}:]+):capture\}\}",
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
        int? startStepOrder = null,
        IReadOnlyDictionary<string, string>? initialCapturedValues = null)
    {
        var capturedValues = initialCapturedValues is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(initialCapturedValues, StringComparer.Ordinal);

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
            if (!persistedStep.IsEnabled)
            {
                Console.Error.WriteLine($"[DAP guide] skipping disabled Step {persistedStep.Order}/{orderedSteps.Length} '{persistedStep.Id}'.");
                continue;
            }

            var isCenteredStep = persistedStep.Target is null
                && persistedStep.Bubble.Placement == BubblePlacement.Center;

            if (!isCenteredStep && persistedStep.Target?.Runtime != TargetRuntime.Web)
                throw new InvalidOperationException(
                    $"Guide Step '{persistedStep.Id}' is not a Web Step and cannot run in WebGuideRuntime.");

            var step = MaterializeRuntimeValues(persistedStep, capturedValues);

            if (step.Capture is not null)
            {
                var captured = await ResolveCaptureAsync(page, step, cancellationToken);
                if (captured is null)
                    throw new InvalidOperationException(
                        $"Guide Step '{step.Id}' declares a Web capture that could not be resolved.");

                capturedValues[step.Id] = captured;
                Console.Error.WriteLine($"[DAP guide] captured runtime value for Step '{step.Id}' as '{captured}'.");
            }

            Console.Error.WriteLine($"[DAP guide] starting Step {stepIndex + 1}/{orderedSteps.Length} '{step.Id}'.");
            await _steps.RunActiveStepAsync(page, step, stepIndex + 1, orderedSteps.Length, cancellationToken);
            Console.Error.WriteLine($"[DAP guide] completed Step {stepIndex + 1}/{orderedSteps.Length} '{step.Id}'.");
        }

        Console.Error.WriteLine("[DAP guide] presenting completion bubble.");
        await _steps.WaitForGuideCompletedDismissalAsync(page, cancellationToken);
        Console.Error.WriteLine("[DAP guide] completion bubble dismissed; Guide finished.");
    }

    private static GuideStep MaterializeRuntimeValues(
        GuideStep step,
        IReadOnlyDictionary<string, string> capturedValues)
    {
        if (step.Target is null)
            return step;

        Locator MaterializeLocator(Locator locator) =>
            locator with { Value = Materialize(locator.Value, step.Id, capturedValues) };

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
        IReadOnlyDictionary<string, string> capturedValues) =>
        RuntimeValueToken.Replace(value, match =>
        {
            var sourceStepId = match.Groups["step"].Value;
            if (!capturedValues.TryGetValue(sourceStepId, out var fragment))
                throw new InvalidOperationException(
                    $"Guide Step '{activeStepId}' references runtime capture from Step '{sourceStepId}', but that Step has not captured one.");

            // Runtime values are substituted into persisted locator text. Escape
            // characters that can terminate a single-quoted CSS attribute value.
            return fragment.Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("'", @"\'", StringComparison.Ordinal);
        });

    public static async Task<string?> CaptureStepValueAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken = default)
    {
        if (step.Capture is null)
            return null;

        return await ResolveCaptureAsync(page, step, cancellationToken);
    }

    private static async Task<string?> ResolveCaptureAsync(
        IPage page,
        GuideStep step,
        CancellationToken cancellationToken)
    {
        var capture = step.Capture
            ?? throw new InvalidOperationException($"Guide Step '{step.Id}' does not declare a capture.");
        if (capture.Runtime != TargetRuntime.Web)
            throw new InvalidOperationException("WebGuideRuntime can capture only Web runtime values.");

        var frame = await ResolveTargetFrameAsync(page, step.Target?.FrameContext, cancellationToken);
        if (frame is null)
            return null;

        string? raw;
        switch (capture.Property.Trim().ToLowerInvariant())
        {
            case "frame-url":
                raw = frame.Url;
                break;
            case "frame-url-fragment":
                raw = Uri.TryCreate(frame.Url, UriKind.Absolute, out var uri) ? uri.Fragment : null;
                break;
            default:
            {
                var locator = capture.Locator.Strategy.Trim().ToLowerInvariant() switch
                {
                    "css" => frame.Locator(capture.Locator.Value),
                    "text" => frame.GetByText(capture.Locator.Value),
                    "label" => frame.GetByLabel(capture.Locator.Value),
                    "role" when Enum.TryParse<AriaRole>(capture.Locator.Value, true, out var role) => frame.GetByRole(role),
                    _ => throw new NotSupportedException(
                        $"Unsupported Web capture locator strategy '{capture.Locator.Strategy}'.")
                };

                if (await locator.CountAsync() != 1)
                    return null;

                raw = capture.Property.Trim().ToLowerInvariant() switch
                {
                    "text" => await locator.TextContentAsync(),
                    "value" => await locator.InputValueAsync(),
                    _ => throw new NotSupportedException(
                        $"Unsupported Web capture property '{capture.Property}'.")
                };
                break;
            }
        }

        if (raw is null)
            return null;
        if (string.IsNullOrEmpty(capture.Pattern))
            return raw;

        var match = Regex.Match(raw, capture.Pattern, RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        return match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
    }

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
