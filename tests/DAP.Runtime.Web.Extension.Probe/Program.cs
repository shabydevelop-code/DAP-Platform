using DAP.Core.Guides;
using DAP.Core.Targets;
using DAP.Runtime.Web.Browser;

using var adapter = new ExtensionWebBrowserAdapter();

var resolved = await adapter.ResolveTargetAsync(TargetDescriptor.Create(
    TargetRuntime.Web, new Locator("css", "#content-frame")));

var missing = await adapter.ResolveTargetAsync(TargetDescriptor.Create(
    TargetRuntime.Web, new Locator("css", "#dap-extension-probe-does-not-exist")));

var ambiguous = await adapter.ResolveTargetAsync(TargetDescriptor.Create(
    TargetRuntime.Web, new Locator("css", "iframe")));

Console.WriteLine($"Resolved probe:  {resolved.Status} ({resolved.MatchCount})");
Console.WriteLine($"NotFound probe:  {missing.Status} ({missing.MatchCount})");
Console.WriteLine($"Ambiguous probe: {ambiguous.Status} ({ambiguous.MatchCount})");

if (resolved.Status != WebTargetResolutionStatus.Resolved || resolved.MatchCount != 1)
    throw new Exception("Expected #content-frame to resolve exactly once.");
if (missing.Status != WebTargetResolutionStatus.NotFound || missing.MatchCount != 0)
    throw new Exception("Expected missing probe target to return NotFound.");
if (ambiguous.Status != WebTargetResolutionStatus.Ambiguous || ambiguous.MatchCount < 2)
    throw new Exception("Expected iframe probe target to return Ambiguous.");

Console.WriteLine("PASS: Extension ResolveTargetAsync returned Resolved / NotFound / Ambiguous without Playwright.");


var contextTarget = TargetDescriptor.Create(
    TargetRuntime.Web,
    new Locator("css", "body"));

var urlContainsStep = new GuideStep(
    "probe-context-url", 1, contextTarget, new BubbleDefinition("probe"),
    Context: new StepContextDefinition("url-contains", "localhost:5200"));

var cssExistsStep = new GuideStep(
    "probe-context-css", 2, contextTarget, new BubbleDefinition("probe"),
    Context: new StepContextDefinition("css-exists", "#content-frame"));

var cssMissingStep = new GuideStep(
    "probe-context-missing", 3, contextTarget, new BubbleDefinition("probe"),
    Context: new StepContextDefinition("css-exists", "#dap-context-probe-does-not-exist"));

var urlActive = await adapter.IsContextActiveAsync(urlContainsStep);
var cssActive = await adapter.IsContextActiveAsync(cssExistsStep);
var cssInactive = await adapter.IsContextActiveAsync(cssMissingStep);

Console.WriteLine($"Context url-contains: {urlActive}");
Console.WriteLine($"Context css-exists:   {cssActive}");
Console.WriteLine($"Context css-missing:  {cssInactive}");

if (!urlActive) throw new Exception("Expected localhost URL context to be active.");
if (!cssActive) throw new Exception("Expected #content-frame CSS context to be active.");
if (cssInactive) throw new Exception("Expected missing CSS context to be inactive.");

Console.WriteLine("PASS: Extension IsContextActiveAsync returned active / inactive browser context facts without Playwright.");


var stableStep = new GuideStep(
    "probe-stability", 4, contextTarget, new BubbleDefinition("probe"));
var stable = await adapter.IsStableForPresentationAsync(
    stableStep, TimeSpan.FromMilliseconds(250));
Console.WriteLine($"DOM quiet window:     {stable}");
if (!stable) throw new Exception("Expected TestCRM DOM to become quiet for 250 ms.");
Console.WriteLine("PASS: Extension IsStableForPresentationAsync observed a 250 ms DOM quiet window without Playwright.");


var validationTarget = TargetDescriptor.Create(
    TargetRuntime.Web,
    new Locator("css", "#customer-search input[name=\"name\"]"));
var validationStep = new GuideStep(
    "probe-validation", 5, validationTarget, new BubbleDefinition("probe"),
    Validation: new ValidationDefinition("value-not-empty"));

Console.WriteLine();
Console.WriteLine("Validation probe is armed on the customer-name search field.");
Console.WriteLine("In TestCRM, type any value in the customer-name field. Do not leave the field yet.");
Console.WriteLine("The probe must remain waiting until you press Tab or otherwise blur the field.");

var commit = await adapter.WaitForValidationCommitAsync(validationStep);
if (commit is null) throw new Exception("Expected validation target to resolve exactly once.");
Console.WriteLine($"Validation commit:     {commit.Kind}");

var validationSatisfied = await adapter.IsPrimaryValidationSatisfiedAsync(validationStep);
Console.WriteLine($"Validation satisfied:  {validationSatisfied}");
if (!validationSatisfied) throw new Exception("Expected non-empty customer-name value to satisfy validation after blur.");

await adapter.ConsumeValidationCommitAsync(validationStep);
Console.WriteLine("PASS: Extension reported the natural blur commit and DAP evaluated value-not-empty without Playwright.");


var equalsStep = validationStep with {
    Id = "probe-value-equals",
    Validation = new ValidationDefinition("value-equals", "DAP")
};
Console.WriteLine();
Console.WriteLine("Value-equals proof: enter exactly DAP but KEEP FOCUS in the field for at least 2 seconds.");
Console.WriteLine("The probe will first prove that typing alone does not produce a commit.");
await adapter.ArmValidationAsync(equalsStep);
using (var earlyCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
{
    try
    {
        var early = await adapter.WaitForValidationCommitAsync(equalsStep, earlyCts.Token);
        if (early is not null)
            throw new Exception("Validation committed before blur/Tab.");
    }
    catch (OperationCanceledException) when (earlyCts.IsCancellationRequested)
    {
        Console.WriteLine("PASS: No validation commit occurred while the edited text field retained focus.");
    }
}
Console.WriteLine("Now press Tab or otherwise leave the field.");
var equalsCommit = await adapter.WaitForValidationCommitAsync(equalsStep);
if (equalsCommit is null) throw new Exception("Expected value-equals commit after blur.");
if (!await adapter.IsPrimaryValidationSatisfiedAsync(equalsStep))
    throw new Exception("Expected exact value DAP to satisfy value-equals after blur.");
await adapter.ConsumeValidationCommitAsync(equalsStep);
Console.WriteLine("PASS: DAP evaluated value-equals after the natural blur commit.");

var completionStep = new GuideStep(
    "probe-completion", 6, contextTarget, new BubbleDefinition("probe"),
    CompletionConditions: new[] {
        new StepCompletionCondition("target-exists", validationTarget),
        new StepCompletionCondition("target-enabled", validationTarget),
        new StepCompletionCondition("value-equals", validationTarget, "DAP"),
        new StepCompletionCondition("target-not-exists", TargetDescriptor.Create(
            TargetRuntime.Web, new Locator("css", "#dap-completion-probe-missing")))
    });
if (!await adapter.AreCompletionConditionsSatisfiedAsync(completionStep))
    throw new Exception("Expected extension completion conditions to be satisfied.");
Console.WriteLine("PASS: Extension completion conditions passed without Playwright.");

var captureStep = new GuideStep(
    "probe-capture", 7, validationTarget, new BubbleDefinition("probe"),
    Capture: new StepCaptureDefinition(
        TargetRuntime.Web, new Locator("css", "#customer-search input[name=\"name\"]"), "value", "^(DAP)$"));
var captured = await adapter.CaptureAsync(captureStep);
Console.WriteLine($"Captured value:        {captured}");
if (captured != "DAP") throw new Exception("Expected capture value DAP.");
Console.WriteLine("PASS: Extension capture returned and regex-processed the browser value without Playwright.");


var contentFrame = new FrameContext(new[] { new Locator("css", "#content-frame") });
var framedNameTarget = TargetDescriptor.Create(
    TargetRuntime.Web,
    new Locator("css", "#customer-search input[name=\"name\"]"),
    frameContext: contentFrame);
var framedResolution = await adapter.ResolveTargetAsync(framedNameTarget);
Console.WriteLine($"FrameContext target:   {framedResolution.Status} ({framedResolution.MatchCount})");
if (framedResolution.Status != WebTargetResolutionStatus.Resolved || framedResolution.MatchCount != 1)
    throw new Exception("Expected customer-name target to resolve through #content-frame.");
var wrongFrameTarget = TargetDescriptor.Create(
    TargetRuntime.Web,
    new Locator("css", "#customer-search input[name=\"name\"]"),
    frameContext: new FrameContext(new[] { new Locator("css", "#header-frame") }));
var wrongFrameResolution = await adapter.ResolveTargetAsync(wrongFrameTarget);
if (wrongFrameResolution.Status != WebTargetResolutionStatus.NotFound)
    throw new Exception("Expected customer-name target not to resolve through #header-frame.");
Console.WriteLine("PASS: Extension resolved exact FrameContext paths without scanning unrelated frames.");

var clickTarget = TargetDescriptor.Create(
    TargetRuntime.Web,
    new Locator("css", "#customer-search button.primary"),
    frameContext: contentFrame);
var clickStep = new GuideStep(
    "probe-clicked", 8, clickTarget, new BubbleDefinition("probe"),
    Validation: new ValidationDefinition("clicked"));
Console.WriteLine();
Console.WriteLine("Clicked probe: click the Search button. The content frame may navigate/reload.");
var clickCommit = await adapter.WaitForValidationCommitAsync(clickStep);
if (clickCommit is null || clickCommit.Kind != "clicked")
    throw new Exception("Expected clicked validation commit.");
if (!await adapter.IsPrimaryValidationSatisfiedAsync(clickStep))
    throw new Exception("Expected DAP to retain clicked completion after browser document changes.");
await adapter.ConsumeValidationCommitAsync(clickStep);
Console.WriteLine("PASS: Clicked validation survived the browser action and remained owned by DAP.");
