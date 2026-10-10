# Active Architecture Decisions

1. **Current-state documentation:** Markdown describes only the active implementation and binding rules. Git provides history.
2. **Closed applications:** The learner must not require target application source, private APIs, business databases, or runtime AI.
3. **Persisted Guides:** The database is the execution source of truth. The runtime, not a test driver, owns validation, completion, and Step advancement.
4. **Deterministic targets:** Resolve using persisted descriptors, anchors, and context. Reject missing or ambiguous matches.
5. **Runtime-neutral contracts:** Core and data abstractions remain independent of SQLite and platform adapters.
6. **Web adapter:** The production Web path uses GuideMe (Chrome extension), Native Messaging, named pipes, and .NET. No Playwright production dependency.
7. **Windows adapter:** The production Windows path uses UI Automation.
8. **Application contexts:** Resolve an explicitly identified application instance; do not infer identity from arbitrary target matches. Unsupported multi-context behavior must fail explicitly.
9. **Guide launch:** `DAP.exe --guide <GuideId>` selects the runtime from persisted Steps. Manual is the default; `--mode hybrid` applies only persisted supported automation values. There is no start-from-step launch.
10. **Guide Steps:** Disabled Steps keep their identity; centered targetless Steps are valid; the example Guide's final summary is persisted rather than synthesized.
11. **Target ownership:** Guide completion and the bubble's **End assistance** / **סיים ליווי** action end the learner and clean its UI without closing the target application. The same Guide can be launched again.
12. **Demo/test separation:** SampleApp and test-specific drivers must not define product behavior. Example Guides are data, not special cases in runtime code.
13. **No legacy database support:** New code need not import or convert retired database formats. Do not delete or reset current active databases as part of code cleanup.
14. **Timeout policy:** Automated technical waits must not be increased beyond five seconds without explicit approval; human time spent on a Guide Step is not a technical timeout.
15. **User-facing naming:** The Chrome extension display name is **GuideMe**. Internal component names and protocols retain their DAP identifiers.
16. **Deployment:** Publish the Windows x64 framework-dependent learner and Native Messaging host together. Register Chrome against the packaged host rather than source build output or temporary directories.
17. **Web transport lifecycle:** The GuideMe extension currently keeps a Native Messaging connection that may outlive Guide sessions. Do not introduce on-demand connection/disconnection without a reliable reactivation mechanism and regression coverage. An idle native host is not by itself a learner-process leak.
18. **Existing text values:** A text-input Step requires an observed focus-and-leave interaction before evaluating a pre-existing value. No text modification is required; persisted validation and completion conditions must still pass. The same rule applies to Web and Windows, without demo-specific checks.
19. **Instructor:** Authoring tools and AI-assisted diagnosis are future capabilities; the learner remains independent of them.
