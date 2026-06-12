# Design Specification - Batch Export Sets & Cancellation Support

## Context
The "Batch Export Sets" feature in the Virtuart4D Navisworks Plugin did not work correctly for Search Sets, didn't update progress in real-time, failed to export items whose ancestors were hidden in the active document, and lacked a cancellation mechanism to abort the export operation mid-way.

This design document outlines the solutions to:
1. Fix the Search Sets query execution by passing the active Document reference to `FindAll`.
2. Fix ancestor visibility so elements in the selected set are guaranteed to be visible and exportable.
3. Update progress in real-time during the batch export loop.
4. Implement a clean cancellation mechanism via the secondary "Cancel" button on the UI.

## Proposed Changes

### 1. SelectionSetCache.cs
Modify `Collect`, `Coletar`, and `ObterItensDoSet` to propagate the `Document` reference, ensuring `ss.Search.FindAll(doc, false)` executes on the correct document.

### 2. BatchSetExportService.cs
- Add `Func<bool> checkCancel` and change `Action<string> progress` to `Action<int, string> progress` in `ExportSets`.
- Ensure all ancestors of exported elements are shown: `doc.Models.SetHidden(ToModelItemCollection(ancestorSet), false);`.
- Check `checkCancel` in the export loop and break out early if cancellation is requested.

### 3. BatchSetExportForm.cs
- Add `_exporting` and `_cancelRequested` flags.
- Bind the secondary Cancel button to set `_cancelRequested = true` if `_exporting` is active, or close the form otherwise.
- Call `ExportSets` with progress updates and the cancellation check.
- Update `ShowSummary` to report if the operation was cancelled.
