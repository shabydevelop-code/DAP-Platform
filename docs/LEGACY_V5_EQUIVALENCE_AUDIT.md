# Legacy vs V5 canonical guides — read-only audit

The three production sample guides in the original `GuideMe.db` are the baseline.
`GuideMe-V5.db` must not be assumed equivalent merely because its schema is normalized.

## Run

From the repository root in PowerShell:

```powershell
git pull --ff-only origin main
python tools\audit_legacy_vs_v5.py --output "$env:TEMP\guideme-v5-equivalence-audit.json"
```

Read the report:

```powershell
Get-Content "$env:TEMP\guideme-v5-equivalence-audit.json" -Encoding UTF8
```

Both databases are opened using SQLite `mode=ro` and `PRAGMA query_only=ON`.
The audit does **not** update the source or V5, compile or launch a learner,
or perform migrations. The only optional output is a JSON report.

The audit checks the Web, Windows, and mixed canonical guide keys; ordered steps,
bubble text and placement, context fields, primary element locator and iframe
context, primary validation kind/expected value/options, runtime mapping, and
presence of target anchors. Differences are reported by step order.

## Interpretation and limitations

- Exit 0: no differences detected by the **implemented checks**, not proof of full behavioral equivalence.
- Exit 2: discrepancies were detected; inspect the JSON report.
- Exit 1: database/schema/access error.
- The legacy `AdvanceMode` is not a direct column in compact V5. Its semantics
  need separate runtime/compiled-plan comparison.
- Additional completion conditions, captures, all anchor locator details, and
  effective learner execution behavior are not fully covered.
- Some differences may reflect an intentional translation; document and validate
  these explicitly rather than silently rewriting either database.
- Never auto-fix source guides or V5 from this report.

**Next gate:** inspect audit results and extend coverage of uncovered semantics
before declaring the original guides and Instructor-authored V5 interchangeable.
