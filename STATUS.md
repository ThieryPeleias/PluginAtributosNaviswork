# Virtuart4D Navisworks Datasmith Exporter - Handoff & Status Report

This document summarizes the current state of the project, details the architectural changes made to fix the empty mesh/visibility bugs, documents the active diagnostic loop regarding performance and file sizes, and establishes strict guidelines for next steps.

---

## 1. Project Status Summary

- **Compilation:** **100% Successful** (0 errors, 0 warnings) targeting **.NET Framework 4.8** using the dotnet CLI.
- **UI Integration:** Fully operational. Adds the custom **Virtuart4D** ribbon tab and an **Export Datasmith** button.
- **Git Alignment:** Fully configured identical dual remotes (successfully pushed all commits):
  - **Gitea:** `http://192.168.255.109:3003/Virtuart4D/Virtuart4DNavisworks.git`
  - **GitHub:** `https://github.com/ThieryPeleias/Virtuart4DNavisworks.git`

---

## 2. Active Diagnostic Loop (Performance & File Size Bloat)

Despite successful compilation and deployment, the custom exporter is currently stuck in a **performance loop**:
- **The Issue:** `T8.udatasmith` yielded the same slow export speed (~2m 40s) and bloated file size (~12.9 MB udatasmith file) as `T5`.
- **Active Diagnostic Findings:**
  1. **SafeArray CopyTo Optimization:** We successfully optimized the hot-path vertex loop in `DatasmithGeometryCallback.cs` by replacing millions of slow COM `GetValue` reflection calls with fast, native `Array.CopyTo` memory copies. This successfully optimized the CPU coordinate retrieval.
  2. **COM API Performance Bottleneck:** The primary bottleneck is the legacy Navisworks COM API itself. When we call `frag.GenerateSimplePrimitives(...)` on leaf paths that contain many fragments (e.g., roof tiles `Telha` which have 100 fragments each), the COM marshalling loop takes approximately 1.5 to 2 seconds per leaf node, resulting in the overall export delay.
  3. **Vertex & Triangle Deduplication:** We implemented structural world-space deduplication using `VertexKey` and `TriangleKey` hashing inside the callback. While this merges duplicate/adjacent vertices to optimize `.udsmesh` sizes, it does not reduce the number of paths/fragments processed.

---

## 3. Strict Architectural Directive (Structure & Placement)

> [!WARNING]
> **CRITICAL RULE:** Under no circumstances should we restructure the scene graph hierarchy or modify coordinate transformations to solve the performance loop.
> - **Tree Hierarchy:** Grouping parent nodes must map to `FDatasmithFacadeActor`, and concrete leaf geometry nodes must map to `FDatasmithFacadeActorMesh`.
> - **Spatial Placement:** World-space coordinates must be baked directly into the vertices in C# using `frag.GetLocalToWorldMatrix()` inside `DatasmithGeometryCallback.cs`, leaving actors with the identity transform.
> - **Rationale:** This specific configuration guarantees a 100% correct scene tree structure and precise spatial placement inside Unreal Engine. Restructuring or decomposing matrices breaks the positioning and centers meshes at the origin.

---

## 4. Active Code Base Reference Links

- **Core Exporter Service:** [DatasmithExporterService.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/DatasmithExporterService.cs) — Hierarchy traversal, Selection Expansion, and Datasmith Facade SDK integration.
- **Geometry Callback Handler:** [DatasmithGeometryCallback.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/DatasmithGeometryCallback.cs) — Coordinates extraction, scaling, Y-axis inversion, and SafeArray `CopyTo` optimizations.
- **Ribbon UI and Saves Handler:** [Virtuart4DPlugin.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/Virtuart4DPlugin.cs) — Controls Ribbon creation and invokes the file saving dialog.
- **Project Structure:** [Virtuart4DNavisworks.csproj](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/Virtuart4DNavisworks.csproj) — Defines Net48 dependencies and references.
- **Build Script:** [build.bat](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/build.bat) — Automates clean rebuilding.
