# Batch Export Sets and Cancellation Implementation Plan

> **For agentic workers:** Use executing-plans or subagent-driven-development to implement this plan task-by-task.

## Context
This plan implements fixes for Selection/Search Sets collection, updates parent visibility during export, enables real-time progress bar tracking, adds cancellation support, and resolves the GUI deadlock/freeze by launching the form modelessly and executing the Datasmith exporter silently.

## File Structure
- [SelectionSetCache.cs](file:///E:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/SelectionSetCache.cs) (Completed)
- [BatchSetExportService.cs](file:///E:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/BatchSetExportService.cs) (modify)
- [Virtuart4DPlugin.cs](file:///E:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/Virtuart4DPlugin.cs) (modify)
- [BatchSetExportForm.cs](file:///E:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/BatchSetExportForm.cs) (Completed)

## Tasks

### 1. Fix SelectionSetCache (Completed)
- [x] Modify `Collect(Document doc)` to pass `doc` to `Coletar`.
- [x] Modify `Coletar` signature and body to receive `doc` and pass it to `ObterItensDoSet`.
- [x] Modify `ObterItensDoSet` signature and body to receive `doc` and run search sets via `ss.Search.FindAll(doc, false)`.

### 2. Update BatchSetExportService Signature & Parent Visibility (Completed)
- [x] Modify `BatchSetExportService.ExportSets` signature (checkCancel and progress).
- [x] Inside `ExportSets` loop, check `checkCancel` and break early if cancellation is requested.
- [x] Inside `ExportSets` loop, explicitly call `doc.Models.SetHidden(ToModelItemCollection(ancestorSet), false)`.

### 3. Restore useAutomation: true in BatchSetExportService
- [ ] Change the `useAutomation` parameter value in `BatchSetExportService.cs` from `false` back to `true` to ensure the export executes in silent automation mode.
- [ ] Verify compilation by running `dotnet build`.

### 4. Implement Modeless Form Invocation in Virtuart4DPlugin
- [ ] Add `private static BatchSetExportForm _batchForm;` to `Virtuart4DCommandHandler` class in `Virtuart4DPlugin.cs`.
- [ ] Modify `ExecutarBatchExportSets()` to check if `_batchForm` is null or disposed, instantiate it, and show it as modeless using `_batchForm.Show(owner)` instead of `form.ShowDialog(owner)`.
- [ ] Verify compilation by running `dotnet build`.

### 5. Add Cancellation Support and UI Improvements to BatchSetExportForm (Completed)
- [x] Add `private bool _exporting = false;` and `private bool _cancelRequested = false;` fields.
- [x] Bind `btnCancel.Click` to check if `_exporting` is active.
- [x] Implement `OnFormClosing` override.
- [x] Update `BtnExport_Click` to pass callbacks, update progress bar in real-time, and call `ShowSummary`.
- [x] Update `ShowSummary` signature.
