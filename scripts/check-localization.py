"""Fail when known learner UI strings bypass the shared localization catalog."""
import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[1]
localization = root / "src/DAP.App/Localization"
he = json.loads((localization / "he.json").read_text(encoding="utf-8"))
en = json.loads((localization / "en.json").read_text(encoding="utf-8"))
errors = []
for key in sorted(set(he) | set(en)):
    if not he.get(key) or not en.get(key):
        errors.append(f"Missing or empty translation: {key}")
for path in [
    "src/DAP.App/DapApplicationHost.cs",
    "src/DAP.App/GuideSessionTray.cs",
    "src/DAP.App/DapLaunchOptions.cs",
]:
    source = (root / path).read_text(encoding="utf-8")
    for key in re.findall(r'texts\\.(?:Get|Format)\\("([^"]+)"', source):
        if key not in he or key not in en:
            errors.append(f"{path}: missing localized key {key}")
    if re.search(r'(?:MessageBox\\.Show|ToolStripMenuItem)\\(\\s*"(?!GuideMe)', source):
        errors.append(f"{path}: direct UI literal; use localized text")
for path in [
    "src/DAP.Runtime.Web.Extension/content-runtime.js",
    "src/DAP.Runtime.Web/Browser/ExtensionWebBrowserAdapter.cs",
]:
    source = (root / path).read_text(encoding="utf-8")
    for literal in ["גרור להזזת הבועה", '("שלב "+stepNumber', '$"שלב {stepNumber} מתוך']:
        if literal in source:
            errors.append(f"{path}: Hebrew-only UI fallback: {literal}")
if errors:
    print("\\n".join(errors))
    sys.exit(1)
print("Localization guard passed.")
