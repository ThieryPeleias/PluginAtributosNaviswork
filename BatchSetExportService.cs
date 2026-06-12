using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace Virtuart4DNavisworks
{
    public sealed class BatchSetExportResult
    {
        public string SetName { get; set; }
        public int ElementCount { get; set; }
        public string OutputPath { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool HasAttributeWarning { get; set; }
    }

    public static class BatchSetExportService
    {
        public const int DefaultFullMergeDepth = 20;

        public static List<BatchSetExportResult> ExportSets(
            Document doc,
            IEnumerable<string> selectedSetNames,
            string outputFolder,
            int mergeDepth,
            double originX,
            double originY,
            double originZ,
            Action<int, string> progress = null,
            Func<bool> checkCancel = null)
        {
            if (doc == null || doc.IsClear)
                throw new InvalidOperationException("No active Navisworks document.");

            if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
                throw new DirectoryNotFoundException("Output folder does not exist.");

            if (mergeDepth <= 0)
                throw new ArgumentOutOfRangeException(nameof(mergeDepth), "Merge depth must be greater than zero.");

            var selectedNames = new HashSet<string>(
                (selectedSetNames ?? Enumerable.Empty<string>())
                    .Where(nome => !string.IsNullOrWhiteSpace(nome))
                    .Select(nome => nome.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var allSets = SelectionSetCache.Collect(doc)
                .Where(setInfo => !string.IsNullOrWhiteSpace(setInfo?.Nome))
                .ToList();

            var selectedSets = allSets
                .Where(setInfo => selectedNames.Contains(setInfo.Nome))
                .OrderBy(setInfo => setInfo.Nome, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var results = new List<BatchSetExportResult>();
            if (selectedSets.Count == 0)
                return results;

            var originalSelection = new Selection(doc.CurrentSelection);
            var originalHidden = doc.Models.RootItemDescendantsAndSelf
                .Where(item => item.IsHidden)
                .ToList();

            int index = 0;
            foreach (var setInfo in selectedSets)
            {
                if (checkCancel != null && checkCancel())
                {
                    break;
                }

                progress?.Invoke(index, $"Exporting set ({index + 1}/{selectedSets.Count}): {setInfo.Nome}");

                var exportItems = ExpandItems(setInfo.Itens);
                if (exportItems.Count == 0)
                {
                    results.Add(new BatchSetExportResult
                    {
                        SetName = setInfo.Nome,
                        ElementCount = 0,
                        OutputPath = "",
                        Success = false,
                        Message = "Set is empty."
                    });
                    index++;
                    continue;
                }

                var outputPath = BuildOutputPath(outputFolder, setInfo.Nome);

                var attributeResult = AtributoService.GravarAtributos(
                    ToModelItemCollection(exportItems),
                    new List<AtributoCustom>
                    {
                        new AtributoCustom(VirtuartSchema.CategoriaPrincipal, VirtuartSchema.PropriedadeSets, setInfo.Nome)
                    },
                    VirtuartSchema.CategoriaPrincipal);

                var allItems = doc.Models.RootItemDescendantsAndSelf.ToList();
                var exportSuccess = false;
                string exportMessage = "";

                try
                {
                    var exportSet = new HashSet<ModelItem>(exportItems);
                    var ancestorSet = new HashSet<ModelItem>();

                    foreach (var item in exportItems)
                    {
                        var current = item.Parent;
                        while (current != null)
                        {
                            ancestorSet.Add(current);
                            current = current.Parent;
                        }
                    }

                    var itemsToHide = allItems
                        .Where(item => !exportSet.Contains(item) && !ancestorSet.Contains(item))
                        .ToList();

                    var itemsToHideCollection = ToModelItemCollection(itemsToHide);
                    var exportItemsCollection = ToModelItemCollection(exportItems);
                    var selectionItems = new ModelItemCollection();
                    selectionItems.AddRange(exportItems);

                    doc.Models.SetHidden(itemsToHideCollection, true);
                    doc.Models.SetHidden(ToModelItemCollection(ancestorSet), false);
                    doc.Models.SetHidden(exportItemsCollection, false);
                    doc.CurrentSelection.CopyFrom(selectionItems);

                    exportSuccess = DatasmithExporterService.ExportActiveDocument(
                        doc,
                        outputPath,
                        mergeDepth,
                        originX,
                        originY,
                        originZ,
                        "Batch Set Export",
                        setInfo.Nome,
                        setInfo.Nome,
                        null,
                        useAutomation: false);

                    exportMessage = exportSuccess
                        ? $"Exported to: {outputPath}"
                        : "Export failed. Check the export log for details.";
                }
                catch (Exception ex)
                {
                    exportMessage = ex.Message;
                }
                finally
                {
                    try
                    {
                        var allItemsCollection = ToModelItemCollection(allItems);
                        var originalHiddenCollection = ToModelItemCollection(originalHidden);
                        doc.Models.SetHidden(allItemsCollection, false);
                        doc.Models.SetHidden(originalHiddenCollection, true);
                        doc.CurrentSelection.CopyFrom(originalSelection);
                    }
                    catch
                    {
                        // Ignore restore failures so the batch can continue.
                    }
                }

                var result = new BatchSetExportResult
                {
                    SetName = setInfo.Nome,
                    ElementCount = exportItems.Count,
                    OutputPath = outputPath,
                    Success = exportSuccess,
                    Message = exportMessage,
                    HasAttributeWarning = attributeResult.sucesso == 0 || attributeResult.erros > 0
                };

                if (result.HasAttributeWarning)
                {
                    result.Message = $"Attribute warning: {attributeResult.mensagem} | {result.Message}";
                }

                results.Add(result);
                index++;
            }

            return results;
        }

        private static List<ModelItem> ExpandItems(ModelItemCollection items)
        {
            var result = new List<ModelItem>();
            var seen = new HashSet<ModelItem>();

            foreach (ModelItem item in items)
            {
                foreach (ModelItem descendant in item.DescendantsAndSelf)
                {
                    if (seen.Add(descendant))
                        result.Add(descendant);
                }
            }

            return result;
        }

        private static ModelItemCollection ToModelItemCollection(IEnumerable<ModelItem> items)
        {
            var collection = new ModelItemCollection();
            collection.AddRange(items);
            return collection;
        }

        private static string BuildOutputPath(string outputFolder, string setName)
        {
            var baseName = SanitizeFileName(setName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "Set";

            var candidate = Path.Combine(outputFolder, baseName + ".udatasmith");
            var index = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(outputFolder, $"{baseName}_{index}.udatasmith");
                index++;
            }

            return candidate;
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray();

            return new string(chars).Trim().Trim('.', ' ');
        }
    }
}
