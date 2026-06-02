# Virtuart4D Navisworks Datasmith Exporter - Current Status & Architecture

This document summarizes the current state of the project, documenting the active Hybrid Exporter architecture that resolves the coordinate bugs, curved tile instancing, and export slowness in Autodesk Navisworks Manage 2025.

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

## 2. The Hybrid Exporter Architecture (1-Second Speed & Perfect Geometry)

We completely resolved all geometry, scale, rotation, instancing, and speed issues by pivoting from raw C# COM extraction to a high-speed silent C++ delegation model:
1. **Silent Exporter Delegation (`DatasmithExporterService.cs`):**
   - Locates the official Epic Games Datasmith exporter plugin registry ID `"DatasmithNavisworksExporter.EpicGames"`.
   - Invokes its `Execute` method, passing the parsed parameter strings: `filePath`, `Merge`, and `Origin` formatted with invariant culture decimal separators.
   - Leverages Epic's optimized native C++ engine, executing in **~1 second** with perfect coordinate scale/rotation and proper instancing for roof tiles (`Telha`) at merge depth 0.
2. **Metadata Key Normalization (`DatasmithExporterService.cs`):**
   - Epic's native plugin automatically exports all metadata properties but separates category and name using `*` (e.g. `Item*Layer`).
   - Our C# post-processor loads the generated `.udatasmith` XML using `XmlDocument` and normalizes the property keys to the custom `Item.Layer` format in under 50ms by replacing `*` with `.`.
3. **Directory Fallback Scan (`FindMostRecentDatasmithFile`):**
   - Since `Application.IsAutomated` is false in interactive GUI mode, Epic's exporter always shows its native `SaveFileDialog`.
   - We removed the local `SaveFileDialog` from `ExportSettingsForm.cs` so the user interactively works with only ONE dialog.
   - Once the SaveFileDialog completes and Epic exports the model, our plugin gets the Current Working Directory (CWD) of the process (which is automatically updated by the dialog to the selected path) and falls back to My Documents, active doc folder, or Desktop, scanning for the newly created `*.udatasmith` file in the last 40 seconds.
   - Once located, it runs the XML post-processor seamlessly.
4. **Buffered Dynamic Logging & Coordinate Metadata (`DatasmithExporterService.cs`):**
   - To keep log files synchronized and completely non-destructive:
     - All logs are cached in-memory during execution. No files are written or deleted at the beginning of the export run.
     - Once the final export path is resolved, we create and write purely to `<ExportedFileName>_export.log` (e.g. `Maurores_export.log`), overwriting it if it already existed but leaving other files (e.g. `Mauro_export.log`) untouched.
     - If the export fails before the dialog is resolved, the logs are dumped to a default fallback file.
   - We record precise picking metadata under the header:
     - `Merge Depth`
     - `Origin` (formatted with dot separator)
     - `Origin Selection Type` (`Vertex`, `Element Center`, or `Default / Manual`)
     - `Selected Element Name`
     - `Selected Element Unique ID` (uses native Navisworks `InstanceGuid`, common properties GUID/Id/UniqueId/IfcGUID, or a fallback hash).
   - If coordinates in the form are modified manually by the user, the metadata is automatically reset to `"Default / Manual"` to prevent stale descriptions.

---

## 3. Active Code Base Reference Links

- **Core Exporter Service:** [DatasmithExporterService.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/DatasmithExporterService.cs) — Handles locating, loading, and silently executing the official exporter plugin, directory scanning fallback, XML post-processing, and dynamic memory-buffered logging.
- **Settings Dialog UI:** [ExportSettingsForm.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/ExportSettingsForm.cs) — Modeless floating settings form with purple theme, unblocking viewport, selection center picking, high-precision vertex snapping activation, and coordinate tracking logic.
- **Precision Vertex Snap Tool:** [PickPointTool.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/PickPointTool.cs) — Custom interactive `ToolPlugin` with high-precision RGB axis overlay rendering, parent/item description tooltip card, COM-level snapped vertex extraction, and `ModelItem` context propagation.
- **Element Center Snap Tool:** [PickElementCenterTool.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/PickElementCenterTool.cs) — Custom interactive `ToolPlugin` that calculates center coordinates and propagates the clicked `ModelItem` context.
- **Ribbon Entry:** [Virtuart4DPlugin.cs](file:///e:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/Virtuart4DPlugin.cs) — Ribbon registration with WPF icons and modeless timer launch.
