# GuideMe naming migration

## Current state

The product name is **GuideMe**. The production Chrome extension source is `src/GuideMe.Learner.ChromeExtension`; the authoring extension is `src/GuideMe.Instructor.ChromeExtension`.

The historical `GuideMe` prefix remains in .NET project paths, namespaces, assemblies, and certain runtime identifiers. These are **compatibility identifiers**, not the desired product branding.

## Migration rule

Do not perform a global search-and-replace of `GuideMe`. Migrate one compatibility boundary at a time and verify Web, Windows and Mixed regression before the next boundary.

1. **Safe presentation/build naming:** rename build-helper script and user-facing labels. Preserve `GuideMe.exe`, assemblies and native channel names. Completed in initial pass.
2. **Application executable:** inventory launchers, publish scripts, shortcuts, process checks, documentation, and native-host integration. Change `AssemblyName` from `GuideMe` to `GuideMe` only together with all launch/publish/process references, and validate an isolated publish before touching `C:\GuideMe`.
3. **.NET project directories and project filenames:** update all `ProjectReference` paths, build scripts, demo launchers and documentation together. Keep explicit old `AssemblyName` values during this step if binary compatibility is still required.
4. **Namespaces/types/assemblies:** change incrementally, with full compile and runtime verification. Do not conflate folder renames with public CLR identity changes.
5. **Protocol and persistence identifiers:** separately review `com.guideMe.web_runtime`, pipe names, mutex names, existing database migration keys, environment variables and log paths. Retain aliases or coordinated migrations where necessary. Never change these as cosmetic renames.

## Required checks

- `dotnet build src\GuideMe.App\GuideMe.App.csproj -c Release`
- `dotnet build src\GuideMe.Instructor\GuideMe.Instructor.csproj -c Release`
- isolated publish and production package checks
- Web, Windows and Mixed guide regression
- Chrome Learner extension and Native Host registration checks

The first pass does **not** rename `GuideMe.exe`, alter the Native Messaging host name or move the database. Those remain explicit follow-up work.
