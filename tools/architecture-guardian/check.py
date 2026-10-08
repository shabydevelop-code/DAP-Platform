"""Read-only architectural checks; independent of product build/runtime."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[2]
issues = []
docs = ["PROJECT_CONTEXT.md", "ARCHITECTURE.md", "REQUIREMENTS.md",
        "DECISIONS.md", "CURRENT_STATUS.md", "APPLICATION_CONTEXT_ARCHITECTURE.md"]
for name in docs:
    if not (root / name).is_file():
        issues.append(f"Missing authoritative document: {name}")

# Dependency checks apply to production projects only, not historical docs or test tooling.
for project in (root / "src").rglob("*.csproj"):
    content = project.read_text(encoding="utf-8")
    for pattern, label in [
        (r'<PackageReference\s+Include="Microsoft\.Playwright(?:\.|")', "Playwright"),
        (r'<PackageReference\s+Include="Selenium(?:\.|")', "Selenium"),
    ]:
        if re.search(pattern, content, re.I):
            issues.append(f"{project.relative_to(root)}: prohibited production dependency {label}")

# Test/diagnostic rules can be expanded only with evidence of reliable enforcement.
print("Architecture Guardian — deterministic checks")
if issues:
    for issue in issues:
        print(f"FAIL: {issue}")
    sys.exit(1)
print("PASS: required current-state documents and production dependency boundaries")
print("NOTE: This is not an AI review and does not prove architectural compliance.")
