# Virtuart4D Navisworks Datasmith Exporter - Current Status & Architecture

This document summarizes the current state of the project, documenting the active Hybrid Exporter architecture that resolves the coordinate bugs, curved tile instancing, export slowness, and outliner hierarchy issues in Autodesk Navisworks Manage 2025.

---

## 1. Project Status Summary

- **Compilation:** **100% Successful** (0 errors, 0 warnings) targeting **.NET Framework 4.8** using the dotnet CLI.
- **UI Integration & Snapping:** Fully operational. Modeless settings window (`ExportSettingsForm.cs`) unblocks the Navisworks viewport with a 50ms deferred timer. Supports both bounding box Selection Center picking and real-time high-precision Viewport Vertex Snapping (crosshair axes + item metadata tooltip card overlay).
- **Save File Dialog:** Leverages Epic's official, native SaveFileDialog directly, eliminating duplicate file dialog popups in GUI mode.
- **Dynamic Logging & Coordinate Metadata:** Fully implemented. Logs are buffered purely in memory during execution, then written strictly to `<ExportedFileName>_export.log` once the SaveFileDialog path is resolved. Safely overwrites old logs when overwriting existing files while leaving all other log files completely untouched.
- **Git Alignment:** Fully configured remotes synchronized:
  - **Gitea:** `http://192.168.255.109:3003/Virtuart4D/Virtuart4DNavisworks.git`
  - **GitHub:** `https://github.com/ThieryPeleias/Virtuart4DNavisworks.git`

---

## 2. Recent Bug Fixes (Hierarchy & XML Cleanup)

During post-processing of the `.udatasmith` file, we identified and successfully fixed the root causes of the empty folder clutter and duplicate children tags:
1. **Case-Sensitivity Mismatch on `<children>` Tag:**
   - Epic's native exporter produces child container tags named `<children>` (all lowercase `c`). Our post-processor was searching for `"Children"` (uppercase `C`).
   - This caused the script to believe no child container existed, resulting in the creation of duplicate `<Children>` tags containing the smart-merged meshes, while leaving the original `<children>` tags empty.
   - We updated all XML node traversal code to use case-insensitive comparisons: `child.Name.Equals("children", StringComparison.OrdinalIgnoreCase)`.
2. **Pruning of Empty folders and Orphaned MetaData:**
   - Because of the casing mismatch, the `CleanAllEmptyActors` and `PostProcessCleanup` recursive loops failed to find the `<children>` tags and therefore could not identify or prune empty parent folders (like the original unmerged parent folders).
   - Now that casing is fixed, all empty parent folders that had their meshes moved to smart groups are correctly pruned from the XML.
   - Any orphaned `<MetaData>` blocks associated with the pruned empty folders are also removed, preventing database/outliner bloat in Unreal Engine.

---

## 3. The Hybrid Exporter Architecture & Smart Merging Technical Limitations

### Geometry Merging vs. Outliner Grouping
- **Native C++ Mesh Merging:** Epic's C++ plugin performs the physical merging of geometries into single, combined binary `.udsmesh` files during export. There is no mechanism in the Datasmith XML format to physically merge meshes; the XML merely references the compiled `.udsmesh` assets.
- **Why Split-Merging is Constrained:**
  - If we export with **Merge=5**, Epic merges *everything* under that level unconditionally into a single `.udsmesh` file. We cannot separate or regroup these geometries in C# post-processing because the `.udsmesh` files are compiled binary assets.
  - If we export with **Merge=0** (keeping separate), Epic generates separate meshes. Our C# post-processor can successfully restructure the XML hierarchy to group them under new folders based on attribute values (e.g., creating `Viga_Pre-Moldada_ValueY` and `Viga_Pre-Moldada_ValueZ` folders and moving the meshes under them). However, because they are separate `.udsmesh` files, they will import into Unreal Engine as individual separate objects under those outliner folders, not as a single merged mesh.
- **What is Fully Supported Now:**
  - Standard hierarchy merging at any depth (1-5) via native Epic delegation.
  - Attribute-based outliner regrouping (Smart Merging) which groups items under `[ParentLabel]_[PropertyValue]` folders, with perfect pruning of old empty folders and zero XML tag duplication.

---

## 4. Active Code Base Reference Links

- **Core Exporter Service:** [DatasmithExporterService.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/DatasmithExporterService.cs) — Handles locating, loading, and silently executing the official exporter plugin, directory scanning fallback, XML post-processing (including case-insensitive children tag logic and empty folder/metadata pruning), and dynamic memory-buffered logging.
- **Settings Dialog UI:** [ExportSettingsForm.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/ExportSettingsForm.cs) — Modeless floating settings form with purple theme, unblocking viewport, selection center picking, high-precision vertex snapping activation, and coordinate tracking logic.
- **Precision Vertex Snap Tool:** [PickPointTool.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/PickPointTool.cs) — Custom interactive `ToolPlugin` with high-precision RGB axis overlay rendering, parent/item description tooltip card, COM-level snapped vertex extraction, and `ModelItem` context propagation.
- **Element Center Snap Tool:** [PickElementCenterTool.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/PickElementCenterTool.cs) — Custom interactive `ToolPlugin` that calculates center coordinates and propagates the clicked `ModelItem` context.
- **Ribbon Entry:** [Virtuart4DPlugin.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/Virtuart4DPlugin.cs) — Ribbon registration with WPF icons and modeless timer launch.
