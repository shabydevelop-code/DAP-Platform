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
require("anyValueTarget" in ui and "context?.Runtime === 'Windows'" in
        ui[ui.find("const anyValueTarget"):ui.find("const targetValueDescription")],
        "Inspector: Any value display restricted to Web")

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

if failures:
    print("Instructor parity gate FAILED:")
    for failure in failures:
        print(" - " + failure)
    sys.exit(1)
print("Instructor parity static gate passed; integration tests still required.")
