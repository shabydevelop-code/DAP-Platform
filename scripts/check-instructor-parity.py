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
capture_start = ui.find("(!pickingFor.completion && !pickingFor.variableSource && (editableContent")
capture_end = ui.find("(pickingFor.completion ? '<label for=\"target-condition\"", capture_start)
capture_option = ui[capture_start:capture_end] if capture_start >= 0 and capture_end >= 0 else ""
require(bool(capture_option) and
        "requestSession.runtime === 'Web' ?" not in capture_option and
        "requestSession.runtime === 'Web' &&" not in capture_option,
        "Capture UI: Any value option restricted to Web")

# Windows must implement a semantic nonempty matcher, not simply render a
# checkbox. Check for a concrete persisted locator strategy and consumption.
windows_save = server[server.find('else if (runtime == "Windows")', server.find('SavePrimaryTargetRequest request')):]
windows_save = windows_save[:windows_save.find('else return Results.BadRequest')]
require("request.AnyContent" in windows_save,
        "Windows Instructor: Any value not persisted")
require("anyValueTarget" in ui and "context?.Runtime === 'Web' &&" not in
        ui[ui.find("const anyValueTarget"):ui.find("const targetValueDescription")],
        "Inspector: Any value display restricted to Web")

# The Windows runtime must actually consume the new matching semantics.
# This explicit contract remains failing until a tested implementation exists.
windows_runtime = ROOT / "src/GuideMe.Learner.Windows"
sources = "\n".join(p.read_text(encoding="utf-8", errors="replace")
                    for p in windows_runtime.rglob("*.cs")) if windows_runtime.exists() else ""
require("nonempty-text" in sources or "any-nonempty" in sources,
        "Windows Learner: Any value matching missing")

if failures:
    print("Instructor parity gate FAILED:")
    for failure in failures:
        print(" - " + failure)
    sys.exit(1)
print("Instructor parity static gate passed; integration tests still required.")
