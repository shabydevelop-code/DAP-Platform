#!/usr/bin/env python3
"""Static regression gate for GuideMe Instructor runtime parity.

This is intentionally fail-closed: no runtime is declared supported until its
capture, editing, persistence, and Learner matching paths are implemented.
Behavioral tests must supplement this gate.
"""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
ui = (ROOT / "src/GuideMe.Instructor.Web/wwwroot/main.js").read_text(encoding="utf-8")
server = (ROOT / "src/GuideMe.Instructor.Web/Program.cs").read_text(encoding="utf-8")
web = (ROOT / "src/GuideMe.Learner.ChromeExtension/content-runtime.js").read_text(encoding="utf-8")
failures = []

def require(ok, message):
    if not ok:
        failures.append(message)

# Match the actual selectors (the source contains regexes, not necessarily
# a literal `:cell-any(` string).
require("parseCellTextSelector" in web and "cell-any" in web and "nonempty-text" in web
        and "matchesCellText" in web,
        "Web Learner: Any value selector handling missing")
require("picked-content-any" in ui and "buildAny" in ui,
        "Instructor: Any value capture/edit controls missing")

# Inspect only the capture-option expression, not unrelated runtime-specific
# code that happens to be within an arbitrary character window.
capture_start = ui.find("(!pickingFor.completion && !pickingFor.variableSource ?")
capture_end = ui.find("(pickingFor.completion && !pickingFor.completionEdit?.identity ? '<label for=\"target-condition\"", capture_start)
capture_option = ui[capture_start:capture_end] if capture_start >= 0 and capture_end >= 0 else ""
require(bool(capture_option) and
        "requestSession.runtime === 'Web' ?" not in capture_option and
        "requestSession.runtime === 'Web' &&" not in capture_option,
        "Capture UI: Any value option restricted to Web")

# Windows must implement a semantic nonempty matcher, not simply render a
# checkbox. Check for a concrete persisted locator strategy and consumption.
windows_save = server[server.find('else if (runtime == "Windows")', server.find('SavePrimaryTargetRequest request')):]
windows_save = windows_save[:windows_save.find('else return Results.BadRequest')]
require("request.AnyContent" in windows_save and "name-nonempty" in windows_save,
        "Windows Instructor: Any value not persisted")
require("const anyValue = (runtime === 'Web'" in ui and
        "'name-nonempty','automation-id-nonempty'" in ui and
        "content:anyValue ? 'כל ערך'" in ui,
        "Inspector: Any value display restricted to Web or Windows")

# The Windows runtime must actually consume the new matching semantics.
# This explicit contract remains failing until a tested implementation exists.
windows_runtime = ROOT / "src/GuideMe.Runtime.Windows"
sources = "\n".join(p.read_text(encoding="utf-8", errors="replace")
                    for p in windows_runtime.rglob("*.cs")) if windows_runtime.exists() else ""
require("name-nonempty" in sources and "automation-id-nonempty" in sources and
        "HasNonemptyObservedValue" in sources,
        "Windows Learner: Any value matching missing")

# A matching strategy must also work when used as a Self/Ancestor/Sibling
# anchor. The target-only Find() path is not enough: otherwise the learner
# throws 'Unsupported Windows locator strategy' during anchor resolution.
resolver_file = windows_runtime / "Targets/WindowsTargetResolver.cs"
resolver = resolver_file.read_text(encoding="utf-8") if resolver_file.exists() else ""
anchor_match_start = resolver.find("private static bool MatchesLocator(")
anchor_match_end = resolver.find("private static bool MatchesCondition(", anchor_match_start)
anchor_match = (resolver[anchor_match_start:anchor_match_end]
                if anchor_match_start >= 0 and anchor_match_end > anchor_match_start else "")
require("name-nonempty" in anchor_match and
        "automation-id-nonempty" in anchor_match and
        "HasNonemptyObservedValue(element)" in anchor_match and
        anchor_match.find("name-nonempty") < anchor_match.find("CreateCondition(locator)") and
        anchor_match.find("automation-id-nonempty") < anchor_match.find("CreateCondition(locator)"),
        "Windows Learner: Any value anchor matching unsupported")

# Identity qualifiers must be authored and enforced in both learner runtimes.
compiler = (ROOT / "src/GuideMe.Data.Sqlite/Guides/GuideV5ExecutionCompiler.cs").read_text(encoding="utf-8")
writer = (ROOT / "src/GuideMe.Instructor.Web/Data/V5GuideWriter.cs").read_text(encoding="utf-8")
web_learner = (ROOT / "src/GuideMe.Runtime.Web/Learner/WebGuideStepRuntime.cs").read_text(encoding="utf-8")
windows_learner = (ROOT / "src/GuideMe.Runtime.Windows/Learner/WindowsGuideRuntime.cs").read_text(encoding="utf-8")
require("identityItems" in ui and "data-identity-action" in ui and "identity:!!session.completionEdit?.identity" in ui,
        "Instructor: identity guard capture and editor missing")
require("identity: request.Identity" in server and 'bool Identity = false' in server and
        'OptionsJson' in writer and 'bool identity = false' in writer,
        "Instructor: identity guard persistence missing")
require("actions, identity" in compiler and "target-exists" in compiler and
        "IdentityConditions" in web_learner and "IdentityConditions" in windows_learner,
        "Learner: Web/Windows identity guard parity missing")

# Do not reintroduce a raw locator modal for identity guards. Both roles
# must invoke the same semantic value editor and persist via role-specific APIs.
require("function editableLocatorBinding(" in ui and
        "identityUsageId:id" in ui and
        "function editPrimaryTargetValue({kind, step, locators, binding:preparedBinding, identityUsageId = null})" in ui and
        "'/completion-target/variable?completionId='" in ui and
        "'/primary-target/variable'" in ui,
        "Instructor: action/identity target semantic editor must be shared")
require("label:'מזהה הרכיב', value:locator.Value" not in ui and
        "modal('עריכת מזהה אימות" not in ui,
        "Instructor: raw CSS locator editor must not return for identity guards")
require("GuideMeAnyValueUI.renderOption('edit-primary-target-any'" in ui and
        "initializeVariableField(input, variableControls, binding.value)" in ui,
        "Instructor: shared target editor must preserve Any value and variables")

# All screen elements use one presentation model and renderer, irrespective
# of whether they currently act as an action target or an identity guard.
require("function screenElementPresentation(" in ui and
        "function renderScreenElementDetails(" in ui and
        "function renderScreenElementActions(" in ui and
        "const primaryPresentation = screenElementPresentation(" in ui and
        "return {rule, ...screenElementPresentation(" in ui and
        "renderScreenElementDetails(primaryPresentation, true)" in ui and
        "renderScreenElementDetails(item, false, index)" in ui and
        "renderScreenElementActions(true)" in ui and
        "renderScreenElementActions(false, index)" in ui,
        "Instructor: primary and identity screen elements must use the same renderer")
require(ui.count("function renderScreenElementDetails(") == 1 and
        ui.count("function renderScreenElementActions(") == 1 and
        "const targetType =" not in ui and
        "const targetValueDescription =" not in ui,
        "Instructor: duplicate role-specific element presentation is forbidden")

# Prevent destructive removal of the action target while identity guards remain.
require("if (identityItems.length > 0)" in ui and
        "יש להגדיר תחילה רכיב אחר כיעד פעולה" in ui,
        "Instructor: action-target removal must be blocked while identity guards exist")
require("יש להגדיר תחילה רכיב אחר כיעד פעולה" in writer and
        "internal static void RemovePrimaryTarget(" in writer,
        "Persistence: action-target removal must protect identity guards")
require("V5GuideWriter.RemovePrimaryTarget(id, stepId)" in server and
        "return Results.Problem(ex.Message, statusCode: 409)" in server,
        "API: blocked action-target removal must return conflict")

# Primary and identity capture must share the entire authoring form, not
# merely the picker transport. The role changes only the persistence route.
require("(!pickingFor.completion || pickingFor.completionEdit?.identity) && !pickingFor.variableSource" in ui and
        "variableValueFieldMarkup('picked-content-value', 'זיהוי לפי תוכן')" in ui and
        "GuideMeAnyValueUI.renderOption('picked-content-any', false)" in ui and
        "initializeVariableField(input, $('picked-content-value-variables'), initialContentValue)" in ui and
        "session.completionEdit?.identity ? 'add-identity' : 'add-completion'" in ui and
        "screen-target-capture hidden" in ui,
        "Instructor: identity capture must reuse primary content, variable, Any-value fields and separate card")
require("(identity ? '/completion-target' : '/primary-target')" in ui and
        "contentValue,anyContent,selectedTag:candidate.tag" in ui and
        "pickedFor.completion && !pickedFor.completionEdit?.identity" in ui,
        "Instructor: identity capture must save content through its own role")
require("ApplyCapturedContentSelector(selector, request.ContentValue, request.AnyContent == true)" in server and
        "string? ContentValue = null, bool? AnyContent = null" in server and
        'request.AnyContent == true' in server,
        "API: identity capture content and Any-value must persist for Web and Windows")

# Action target selection is exclusive: only identity cards offer the switch;
# the active target has a disabled, non-toggleable marker in the same corner.
require('class="screen-element-role active"' in ui and
        'aria-pressed="true" disabled' in ui and
        'data-identity-action="set-action"' in ui and
        'renderScreenElementDetails(item, false, index)' in ui and
        "button('set-action', 'הגדר כיעד פעולה')" not in ui,
        "Instructor: action-target selection must be exclusive corner control")

if failures:
    print("Instructor parity gate FAILED:")
    for failure in failures:
        print(" - " + failure)
    sys.exit(1)
print("Instructor parity static gate passed; integration tests still required.")
