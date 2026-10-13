"""Lightweight localization check run automatically by the app build.

Checks translation parity and obvious learner-facing literals; not a general
static analyzer. Keep the patterns narrow to avoid false build failures.
"""
import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[1]
catalog = root / "src/DAP.App/Localization"
he = json.loads((catalog / "he.json").read_text(encoding="utf-8"))
en = json.loads((catalog / "en.json").read_text(encoding="utf-8"))
errors = []


def report(path, number, detail):
    errors.append(f"{path}:{number}: localization: {detail}")


for key in sorted(set(he) | set(en)):
    if not he.get(key) or not en.get(key):
        errors.append(f"Localization catalog: missing or empty translation: {key}")

# Only learner UI entry points, not logs, protocol identifiers, or demo apps.
ui_sources = [
    "src/DAP.App/DapApplicationHost.cs",
    "src/DAP.App/GuideSessionTray.cs",
    "src/DAP.App/DapLaunchOptions.cs",
    "src/DAP.Runtime.Windows/Bubbles/WindowsBubblePresenter.cs",
    "src/DAP.Runtime.Web/Browser/ExtensionWebBrowserAdapter.cs",
    "src/DAP.Runtime.Web.Extension/content-runtime.js",
]
key_pattern = re.compile(r'\\b(?:texts|_texts)\\??\\.(?:Get|Format)\\(\\s*"([^"]+)"')
# Direct quoted learner-facing UI values only; variable/DB-sourced text is fine.
cs_literal = re.compile(
    r'\\b(?:Text|ToolTip|Header|Title|Content)\\s*=\\s*"([^"]*[A-Za-z\\u0590-\\u05ff][^"]*)"'
)
js_literal = re.compile(
    r'\\b(?:textContent|innerText|title)\\s*=\\s*"([^"]*[A-Za-z\\u0590-\\u05ff][^"]*)"'
)
known_fallback = re.compile(
    r'(?:\\?\\?|\\|\\|)\\s*(?:\\$?")([^"\\n]*[\\u0590-\\u05ff][^"\\n]*)"'
)

for path in ui_sources:
    source = (root / path).read_text(encoding="utf-8")
    for number, line in enumerate(source.splitlines(), 1):
        for match in key_pattern.finditer(line):
            key = match.group(1)
            if key not in he or key not in en:
                report(path, number, f"missing translation key: {key}")
        # A small, explicit set of UI assignment sinks avoids flagging identifiers,
        # logging strings, CSS, and protocol payloads.
        if path.endswith(".cs"):
            for match in cs_literal.finditer(line):
                report(path, number, f"hardcoded UI string: {match.group(1)!r}")
            if "MessageBox.Show(" in line and re.search(r'MessageBox\\.Show\\(\\s*"', line):
                report(path, number, "hardcoded MessageBox text")
            if "ToolStripMenuItem(" in line and re.search(r'ToolStripMenuItem\\(\\s*"', line):
                report(path, number, "hardcoded tray menu text")
        else:
            for match in js_literal.finditer(line):
                report(path, number, f"hardcoded UI string: {match.group(1)!r}")
        for match in known_fallback.finditer(line):
            report(path, number, f"Hebrew-only UI fallback: {match.group(1)!r}")

if errors:
    print("\\n".join(errors))
    sys.exit(1)
print("Localization guard passed.")
