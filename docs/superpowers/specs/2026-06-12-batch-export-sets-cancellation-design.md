# Design Specification - Batch Export Sets & Cancellation Support

## Context
The "Batch Export Sets" feature in the Virtuart4D Navisworks Plugin did not work correctly for Search Sets, didn't update progress in real-time, failed to export items whose ancestors were hidden in the active document, and locked/froze Navisworks during export.

We identified two types of freezes:
1. **Modal Deadlock on UI mode (`useAutomation: false`):** Running the exporter in UI mode shows a modal "Export Successful" dialog at the end of the export. Because the `BatchSetExportForm` is opened as a modal dialog (`ShowDialog`), the Epic Games modal popup gets stuck/hidden behind it, preventing user interaction and freezing the application.
2. **COM Automation Deadlock on Automation mode (`useAutomation: true`):** When the form is modal (`ShowDialog`), it runs a nested Win32 message loop. Calling the COM Automation API `Application.Automation.ExecuteAddInPlugin` from inside a nested message loop causes a deadlock in the COM message dispatcher.

### Modeless + Silent Automation Architecture
To solve both deadlocks and achieve a 100% silent, automatic batch export:
1. Open the `BatchSetExportForm` as a **modeless dialog** using `Show(owner)` instead of `ShowDialog(owner)`. This eliminates the nested message loop and lets the COM message pump run normally.
2. Run the Epic Games Datasmith exporter with **`useAutomation: true`**. This invokes `ExecuteAddInPlugin`, which runs the exporter completely silently, suppressing all completion dialogs and export report popups.

## Proposed Changes

### 1. SelectionSetCache.cs
Modify `Collect`, `Coletar`, and `ObterItensDoSet` to propagate the `Document` reference, ensuring `ss.Search.FindAll(doc, false)` executes on the correct document. (Completed)

### 2. BatchSetExportService.cs
- Pass `Func<bool> checkCancel` and `Action<int, string> progress` to `ExportSets`. (Completed)
- Ensure all ancestors of exported elements are shown: `doc.Models.SetHidden(ToModelItemCollection(ancestorSet), false);`. (Completed)
- Check `checkCancel` in the export loop and break out early if cancellation is requested. (Completed)
- Set **`useAutomation: true`** in the `ExportActiveDocument` call so it runs silently through `ExecuteAddInPlugin`.

### 3. Virtuart4DPlugin.cs
- Add `private static BatchSetExportForm _batchForm;` to `Virtuart4DCommandHandler` to keep a reference to the modeless form.
- Modify `ExecutarBatchExportSets()` to open `BatchSetExportForm` as a modeless form (`_batchForm.Show(owner)`) rather than modal (`ShowDialog`).

### 4. BatchSetExportForm.cs
- Add `_exporting` and `_cancelRequested` flags. (Completed)
- Bind the secondary Cancel button to set `_cancelRequested = true` if `_exporting` is active, or close the form otherwise. (Completed)
- Call `ExportSets` with progress updates and the cancellation check. (Completed)
- Update `ShowSummary` to report if the operation was cancelled. (Completed)
