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
