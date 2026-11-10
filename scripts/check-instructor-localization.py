#!/usr/bin/env python3
"""Audit Instructor UI Hebrew literals against the English interface dictionary.

The report is intentionally non-blocking: not every Hebrew string is UI text.
Run with --strict to fail if a UI candidate is missing from the dictionary.
"""
from pathlib import Path
import re
import sys
import json

root = Path(__file__).resolve().parents[1]
web = root / "src/GuideMe.Instructor.Web/wwwroot"
catalog_dir = root / "src/GuideMe.Instructor.Web/Localization"
english = json.loads((catalog_dir / "en.json").read_text(encoding="utf-8"))
hebrew_catalog = json.loads((catalog_dir / "he.json").read_text(encoding="utf-8"))
sources = [web / name for name in ("main.js", "any-value-ui.js")]
hebrew = re.compile(r"[\u0590-\u05ff]")
dictionary_keys = set(english)
catalog_errors = []
if set(english) != set(hebrew_catalog):
    catalog_errors.append("Hebrew and English catalog keys differ")
for key in english:
    if not isinstance(english[key], str) or not english[key]:
        catalog_errors.append(f"Missing English translation: {key}")
    if not isinstance(hebrew_catalog.get(key), str) or not hebrew_catalog.get(key):
        catalog_errors.append(f"Missing Hebrew translation: {key}")
# This is a source-level audit; values built through concatenation require manual review.
# Match one JavaScript string token at a time, honoring escaped quotes.
# The previous regex crossed adjacent literals and reported code fragments.
quoted = re.compile(r"""(['"])((?:\\.|(?!\1)[^\\\r\n])*)\1""")
missing = []
for source in sources:
    for number, line in enumerate(source.read_text(encoding="utf-8").splitlines(), 1):
        for match in quoted.finditer(line):
            # Preserve leading/trailing spaces: they are significant for
            # translated fragments concatenated with dynamic values.
            candidate = match.group(2).replace(r"\'", "'").replace(r'\"', '"').replace(r'\n', '\n')
            if not hebrew.search(candidate) or candidate in dictionary_keys:
                continue
            if len(candidate) > 240 or candidate.startswith(("/", "<")) or "<" in candidate or ">" in candidate:
                continue
            missing.append((source.name, number, candidate))
# HTML is parsed separately: the previous quoted-string regex incorrectly
# treated entire markup lines as one string, inflating the audit count.
from html.parser import HTMLParser
class VisibleText(HTMLParser):
    def __init__(self):
        super().__init__()
        self.line = 0
    def handle_data(self, data):
        value = data.strip()
        if hebrew.search(value) and value not in dictionary_keys:
            missing.append(("index.html", self.getpos()[0], value))
    def handle_starttag(self, tag, attrs):
        for name, value in attrs:
            if name in ("placeholder", "aria-label", "title") and value and hebrew.search(value) and value not in dictionary_keys:
                missing.append(("index.html", self.getpos()[0], value))
VisibleText().feed((web / "index.html").read_text(encoding="utf-8"))
print(f"Instructor localization audit: {len(dictionary_keys)} dictionary entries, "
      f"{len(missing)} untranslated source literals/candidates, {len(catalog_errors)} catalog errors")
for file, line, candidate in missing:
    print(f"{file}:{line}: {candidate[:120]}")
for error in catalog_errors:
    print("Catalog: " + error)
if "--strict" in sys.argv and (missing or catalog_errors):
    sys.exit(1)
