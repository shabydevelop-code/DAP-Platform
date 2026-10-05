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
