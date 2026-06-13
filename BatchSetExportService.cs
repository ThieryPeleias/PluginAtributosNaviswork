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

        public static BatchSetExportResult ExportSingleSet(
            Document doc,
            string setName,
            string outputFolder,
            int mergeDepth,
            double originX,
            double originY,
            double originZ,
            List<ModelItem> allItems)
        {
            if (doc == null || doc.IsClear)
                throw new InvalidOperationException("No active Navisworks document.");

            var allSets = SelectionSetCache.Collect(doc)
                .Where(setInfo => !string.IsNullOrWhiteSpace(setInfo?.Nome))
                .ToList();

            var setInfo = allSets.FirstOrDefault(s => string.Equals(s.Nome, setName, StringComparison.OrdinalIgnoreCase));
            if (setInfo == null)
            {
                return new BatchSetExportResult
                {
                    SetName = setName,
                    ElementCount = 0,
                    OutputPath = "",
                    Success = false,
                    Message = "Set not found in document."
                };
            }

            var exportItems = ExpandItems(setInfo.Itens);
            if (exportItems.Count == 0)
            {
                return new BatchSetExportResult
                {
                    SetName = setName,
                    ElementCount = 0,
                    OutputPath = "",
                    Success = false,
                    Message = "Set is empty."
                };
            }

            var outputPath = BuildOutputPath(outputFolder, setName);

            var attributeResult = AtributoService.GravarAtributos(
                ToModelItemCollection(exportItems),
                new List<AtributoCustom>
                {
                    new AtributoCustom(VirtuartSchema.CategoriaPrincipal, VirtuartSchema.PropriedadeSets, setName)
                },
                VirtuartSchema.CategoriaPrincipal);

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

                // Isolate this set's elements
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
                    setName,
                    setName,
                    null,
                    useAutomation: true);

                exportMessage = exportSuccess
                    ? $"Exported to: {outputPath}"
                    : "Export failed. Check the export log for details.";
            }
            catch (Exception ex)
            {
                exportMessage = ex.Message;
            }

            var result = new BatchSetExportResult
            {
                SetName = setName,
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

            return result;
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
