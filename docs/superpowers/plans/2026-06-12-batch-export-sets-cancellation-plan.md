# Batch Export Sets and Cancellation Implementation Plan

> **For agentic workers:** Use executing-plans or subagent-driven-development to implement this plan task-by-task.

## Context
This plan implements fixes for Selection/Search Sets collection, updates parent visibility during export, enables real-time progress bar tracking, and adds cancellation support.

## File Structure
- [SelectionSetCache.cs](file:///E:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/SelectionSetCache.cs) (modify)
- [BatchSetExportService.cs](file:///E:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/BatchSetExportService.cs) (modify)
- [BatchSetExportForm.cs](file:///E:/@Virtuart/Claude/Projetos/Virtuart4DNavisworks/BatchSetExportForm.cs) (modify)

## Tasks

### 1. Fix SelectionSetCache
- [ ] Modify `Collect(Document doc)` to pass `doc` to `Coletar`.
- [ ] Modify `Coletar` signature and body to receive `doc` and pass it to `ObterItensDoSet`.
- [ ] Modify `ObterItensDoSet` signature and body to receive `doc` and run search sets via `ss.Search.FindAll(doc, false)`.
- [ ] Verify compilation by running `dotnet build`.

### 2. Update BatchSetExportService Signature & Parent Visibility
- [ ] Modify `BatchSetExportService.ExportSets` signature:
  - Add `Func<bool> checkCancel = null`.
  - Change progress callback to `Action<int, string> progress = null`.
- [ ] Inside `ExportSets` loop, check `checkCancel` and break early if cancellation is requested.
- [ ] Inside `ExportSets` loop, explicitly call `doc.Models.SetHidden(ToModelItemCollection(ancestorSet), false)` to ensure parent visibility.
- [ ] Verify compilation by running `dotnet build`.

### 3. Add Cancellation Support and UI Improvements to BatchSetExportForm
- [ ] Add `private bool _exporting = false;` and `private bool _cancelRequested = false;` fields to `BatchSetExportForm`.
- [ ] Bind `btnCancel.Click` in `MontarLayout` to check if `_exporting` is active. If so, set `_cancelRequested = true` and update the status label. If not, close the form.
- [ ] Implement `OnFormClosing` override to cancel the closing event and set `_cancelRequested = true` if `_exporting` is active.
- [ ] Update `BtnExport_Click` to:
  - Initialize `_exporting = true` and `_cancelRequested = false`.
  - Pass the cancellation callback `() => _cancelRequested` and progress callback `(index, message)` to `ExportSets`.
  - Inside progress callback, update `_progressBar.Value` and `_lblStatus.Text` in real-time.
  - Call `ShowSummary` with the list of results and a `cancelled` boolean flag.
  - Reset `_exporting = false` in the `finally` block.
- [ ] Update `ShowSummary` signature to accept `bool cancelled`. Adjust the header message accordingly.
- [ ] Verify compilation by running `dotnet build`.
