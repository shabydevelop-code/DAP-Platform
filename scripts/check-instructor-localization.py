#!/usr/bin/env python3
"""Audit Instructor UI Hebrew literals against the English interface dictionary.

The report is intentionally non-blocking: not every Hebrew string is UI text.
Run with --strict to fail if a UI candidate is missing from the dictionary.
"""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
web = root / "src/GuideMe.Instructor.Web/wwwroot"
dictionary = (web / "instructor-language.js").read_text(encoding="utf-8")
sources = [web / name for name in ("index.html", "main.js", "any-value-ui.js")]
hebrew = re.compile(r"[\u0590-\u05ff]")
dictionary_keys = set(re.findall(r'^\s*"([^"]*[\u0590-\u05ff][^"]*)"\s*:', dictionary, re.M))
# This is a source-level audit; values built through concatenation require manual review.
quoted = re.compile(r"""(['"])([^\r\n]*?[\u0590-\u05ff][^\r\n]*?)\1""")
missing = []
for source in sources:
    for number, line in enumerate(source.read_text(encoding="utf-8").splitlines(), 1):
        for match in quoted.finditer(line):
            candidate = match.group(2).strip()
            if not candidate or candidate in dictionary_keys:
                continue
            # Ignore regexes and HTML attribute fragments; review them separately.
            if len(candidate) > 240 or candidate.startswith(("/", "<")):
                continue
            missing.append((source.name, number, candidate))
print(f"Instructor localization audit: {len(dictionary_keys)} dictionary entries, "
      f"{len(missing)} untranslated source literals/candidates")
for file, line, candidate in missing:
    print(f"{file}:{line}: {candidate[:120]}")
if "--strict" in sys.argv and missing:
    sys.exit(1)
