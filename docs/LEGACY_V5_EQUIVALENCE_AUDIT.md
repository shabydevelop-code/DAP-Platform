# Canonical guide integrity audit (V5)

The current installation uses one database:
`C:\ProgramData\GuideMe\Data\GuideMe-V5.db`.
The three existing guides in V5 are the **Source of Truth**.

## Read-only structural audit

```powershell
git pull --ff-only origin main
python tools\audit_v5_canonical.py --output "$env:TEMP\guideme-v5-integrity.json"
Get-Content "$env:TEMP\guideme-v5-integrity.json" -Encoding UTF8
```

The script opens the existing database with SQLite `mode=ro` and
`PRAGMA query_only=ON`. It checks all three guide keys, foreign keys,
step order, context references and runtime alignment, element locators,
frame paths, anchors, step targets, primary validations, captures and JSON.
No tables or guide data are changed. Report output is outside the database.

Exit codes: 0 = no detected structural issues; 2 = detected issues;
1 = database/schema error. **Zero issues is not proof of semantic
equivalence or correct learner execution.**

## Validate the actual learner compilation

Run the existing production validator for all three guides:

```powershell
dotnet build src\GuideMe.App\GuideMe.App.csproj -c Release
.\scripts\Validate-GuideMeV5.ps1 -DatabasePath "C:\ProgramData\GuideMe\Data\GuideMe-V5.db"
```

Compilation checks the runtime contract but does not execute GUI steps.
A later behavior-level comparison is still needed before declaring
complete equivalence of original Web, Windows and mixed workflows.

The previous `tools/audit_legacy_vs_v5.py` assumes a separate
`GuideMe.db` and is **not applicable** to the current installation.
Do not run it unless explicitly auditing a separate historical database.
