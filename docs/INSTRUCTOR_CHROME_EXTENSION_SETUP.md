# Instructor Chrome extension — isolated authoring setup

The Instructor Chrome extension is **not** the production Learner extension. The two have independent manifests, service workers, and extension IDs.

## Local development (Windows)

1. Pull `main`.
2. Build/run `src/GuideMe.Instructor/GuideMe.Instructor.csproj`.
3. In Chrome, open `chrome://extensions`, enable Developer mode, and select **Load unpacked**.
4. Choose the **source directory** `src/GuideMe.Instructor.ChromeExtension`. Do not select `C:\GuideMe\ChromeExtension`, which is the Learner.
5. Open the target web application tab, click the **GuideMe Instructor** extension icon, paste the pairing token shown in the Instructor desktop window, and select Connect.
6. Start a step in the desktop app, click/change a control in the target page, then end the step. The review displays captured events; the V5 save action remains disabled.

This MVP injects its capture script into the currently selected tab and existing frames when paired. Full navigation reinjection, before/after DOM snapshots, completion-condition inference and V5 persistence are **not yet implemented**.

## Restore a previously modified Learner deployment

If the old Instructor-in-Learner extension files were previously copied into `C:\GuideMe\ChromeExtension`, update the deployed Learner manifest and worker entry from their restored sources and remove only the four obsolete authoring assets. **Do not remove the Learner's other assets.** Reload the Learner extension afterwards.

```powershell
$root = "C:\yossi\ChatGpt\DAP-Platform"
$learnerSource = Join-Path $root "src\GuideMe.Learner.ChromeExtension"
$learnerTarget = "C:\GuideMe\ChromeExtension"
Copy-Item (Join-Path $learnerSource "manifest.json") (Join-Path $learnerTarget "manifest.json") -Force
Copy-Item (Join-Path $learnerSource "service-worker-entry.js") (Join-Path $learnerTarget "service-worker-entry.js") -Force
foreach ($name in @("instructor-capture.js", "instructor-bridge.js", "instructor-popup.html", "instructor-popup.js")) {
    $obsolete = Join-Path $learnerTarget $name
    if (Test-Path -LiteralPath $obsolete -PathType Leaf) { Remove-Item -LiteralPath $obsolete -Force }
}
```

No production runtime database changes are part of this separation.
