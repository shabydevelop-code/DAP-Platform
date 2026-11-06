#!/usr/bin/env python3
"""GuideMe Instructor cross-runtime regression gate.

Fail closed until both runtimes explicitly implement Any value matching.
This is a focused guard, not a substitute for integration tests.
"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / "src/GuideMe.Instructor.Web/wwwroot/main.js"
WEB = ROOT / "src/GuideMe.Learner.ChromeExtension/content-runtime.js"
WIN = ROOT / "src/GuideMe.Learner.Windows"

def check(condition, message):
    if not condition:
        failures.append(message)

failures = []
ui = UI.read_text(encoding="utf-8")
web = WEB.read_text(encoding="utf-8")
check("picked-content-any" in ui, "Capture UI: Any value option missing")
check("buildAny" in ui, "Edit UI: Any value option missing")
check(":cell-any(" in web and ":nonempty-text" in web, "Web Learner: Any value matching missing")

# A shared UI must not restrict the capture option to the Web runtime.
capture = ui[ui.find("picked-content-any") - 2500:ui.find("picked-content-any") + 2500]
check("runtime === 'Web'" not in capture and "Runtime === 'Web'" not in capture,
      "Capture UI: Any value is gated to Web")

# The Windows matching contract must be explicit and testable, not assumed
# from a checkbox in the shared Instructor. This marker is deliberately absent
# until a real Windows Learner implementation and its tests are added.
windows_sources = "\n".join(p.read_text(encoding="utf-8", errors="replace")
                            for p in WIN.rglob("*.cs")) if WIN.exists() else ""
check("GuideMeAnyNonEmptyValue" in windows_sources,
      "Windows Learner: nonempty-value matching contract not implemented")

if failures:
    print("Instructor parity gate FAILED:")
    for failure in failures:
        print(" - " + failure)
    sys.exit(1)
print("Instructor parity static gate passed (runtime tests still required).")
