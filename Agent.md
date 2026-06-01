# CLAUDE.md — Virtuart4D Plugin

Navisworks plugin for Export datasmith with attributes .

**Stage: PROTOTYPE** — ZERO backwards-compat. Rename, delete, restructure freely. Old saves WILL break — user re-Applies from scratch. NEVER implement migration code, PostLoad fixups, or deprecation shims for old data formats. Plan includes migration → REJECT that part, flag before coding.

Rules apply every task unless explicitly overridden. Bias: caution over speed on non-trivial work.

## Git Push Rule — MANDATORY, EVERY COMMIT
After every `git commit`, push to BOTH remotes:
```
git push gitea main
git push virtuart-github main
```
Never push only to Gitea. GitHub (`virtuart-github`) must always be in sync.

## Rule 1 — Think Before Coding
State assumptions explicitly. Ask, don't guess. Push back when simpler approach exists. Stop when confused.

## Rule 2 — Simplicity First
Minimum code. Nothing speculative. No abstractions for single-use code.

## Rule 3 — Surgical Changes
Touch only what you must. Don't improve adjacent code. Match existing style. Don't refactor what isn't broken.

## Rule 4 — Goal-Driven Execution
Define success criteria. Loop until verified. Strong criteria let Claude loop independently.

## Rule 5 — Use the model only for judgment calls
Use for: classification, drafting, summarization, extraction.
Not for: routing, retries, deterministic transforms. Code can answer → code answers.

## Rule 6 — Token budgets are not advisory
Per-task: 4,000 tokens. Per-session: 30,000 tokens. Approaching budget → summarize and start fresh. Surface breach. No silent overrun.

## Rule 7 — Surface conflicts, don't average them
Two patterns contradict → pick one (more recent / more tested). Explain why. Flag other for cleanup.

## Rule 8 — Read before you write
Before adding code, read exports, immediate callers, shared utilities. Unsure why existing code structured a certain way → ask.

## Rule 9 — Tests verify intent, not just behavior
Tests must encode WHY behavior matters, not just WHAT it does. Test that can't fail when business logic changes is wrong.

## Rule 10 — Checkpoint after every significant step
Summarize what done, what verified, what's left. Don't continue from state you can't describe back.

## Rule 11 — Match the codebase's conventions, even if you disagree
Conformance > taste inside codebase. Convention harmful → surface it. Don't fork silently.

## Rule 12 — Fail loud
"Completed" wrong if anything skipped silently. "Tests pass" wrong if any skipped. Default: surface uncertainty, not hide it.

## Behavior rules (drona23/token-efficient)
- Concise. No sycophantic openers/closing fluff, no emojis, no em-dashes.
- Prefer targeted edits over rewriting whole files.
- Test before declaring done. Deliver exactly what requested.
- Don't guess APIs, versions, flags, commit SHAs, or package names. Verify.
- User instructions always override.
- **All UI text, button labels, tooltips, log messages, and comments must be in English.**

## Project Peculiarities & Technical Constraints

### 1. Target Environment & Compilation
- **Platform:** Autodesk Navisworks Manage 2025.
- **Framework:** Must target **.NET Framework 4.8** (`net48`).
- **Targeting Pack:** Use NuGet `Microsoft.NETFramework.ReferenceAssemblies` in `.csproj` to allow cross-platform compiling using the dotnet CLI (e.g. `dotnet build`).
- **Warning-Free Compilation:** Do **NOT** add managed assembly references to native C++ DLLs like `DatasmithFacadeCSharp.dll` in `.csproj`. Reference only the managed wrapper `DatasmithNavisworksPlugin2025.dll` to keep compilation completely warning-free (eliminating `MSB3246`).

### 2. External SDK Reference Paths
- **Navisworks Manage 2025 APIs:** `C:\Program Files\Autodesk\Navisworks Manage 2025\`
- **Epic Games Datasmith C# Facade SDK:** `C:\ProgramData\Autodesk\ApplicationPlugins\EpicGamesDatasmithExporter.bundle\`

### 3. Geometry Extraction Logic (COM API)
- The Navisworks native .NET API does not expose low-level geometry; use the **COM API** instead (`Autodesk.Navisworks.Api.ComApi.ComApiBridge` to cast selections to `InwOpSelection`).
- Iterate over fragments (`InwOaFragment3`) and call `GenerateSimplePrimitives` by implementing the `InwSimplePrimitivesCB` callback interface.
- Retrieve the fragment's matrix (`GetLocalToWorldMatrix()`) and extract its 16 float elements in row-major format using late-bound dynamic COM binding (`dynamic.Matrix`).
- **Coordinate System Conversion:** 
  - Scale coordinates by `100.0f` to convert from Navisworks meters to Unreal Engine centimeters.
  - Invert the Y-axis (`-y`) to transform from Navisworks Right-Handed Z-Up to Unreal Engine Left-Handed Z-Up coordinate space.

### 4. Scene Graph & Metadata Mapping
- Build the scene graph hierarchically: empty grouping nodes map to `FDatasmithFacadeActor` and items containing geometry map to `FDatasmithFacadeActorMesh`.
- Map **all** properties and custom attributes by concatenating `CategoryName.PropertyName` into strings and creating `FDatasmithFacadeMetaData` objects.
- Associate metadata dynamically with actors using `metaData.SetAssociatedElement(actor)` and add them to the scene using `scene.AddMetaData(metaData)`.

### 5. Autodesk Bundle Layout & Local Deployment
- Local active builds are output to the Autodesk local plugins bundle folder: `%APPDATA%\Autodesk\ApplicationPlugins\Virtuart4DNavisworks.bundle\`.
- Keep `PackageContents.xml` in the bundle's root mapping compatibilities to Navisworks 2025 series `Nw22` and pointing to `Contents/v22/Virtuart4DNavisworks.dll`.

