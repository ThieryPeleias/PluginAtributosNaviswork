using System;
using System.IO;
using System.Xml;
using Autodesk.Navisworks.Api;

namespace Virtuart4DNavisworks
{
    /// <summary>
    /// Hybrid exporter service that delegates geometry generation to Epic's official Datasmith exporter
    /// and post-processes the output to normalize custom property naming.
    /// </summary>
    public static class DatasmithExporterService
    {
        private static string _logPath;

        /// <summary>
        /// Writes a timestamped message to both the debug console and the export log file.
        /// </summary>
        private static void Log(string message)
        {
            try
            {
                string logMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                System.Diagnostics.Debug.Write(logMsg);
                if (!string.IsNullOrEmpty(_logPath))
                {
                    File.AppendAllText(_logPath, logMsg);
                }
            }
            catch { }
        }

        /// <summary>
        /// Exports the active Navisworks document to a Datasmith file using the hybrid delegation pipeline.
        /// </summary>
        /// <param name="doc">The active Navisworks Document.</param>
        /// <param name="filePath">Target .udatasmith file path.</param>
        /// <param name="mergeMaxDepth">The maximum hierarchy depth to merge subtrees (0 = no merging).</param>
        /// <param name="originX">Custom origin X coordinate in Navisworks space.</param>
        /// <param name="originY">Custom origin Y coordinate in Navisworks space.</param>
        /// <param name="originZ">Custom origin Z coordinate in Navisworks space.</param>
        /// <returns>True if export was successful, false otherwise.</returns>
        public static bool ExportActiveDocument(
            Document doc, 
            string filePath, 
            int mergeMaxDepth = 0, 
            double originX = 0, 
            double originY = 0, 
            double originZ = 0)
        {
            if (doc == null || string.IsNullOrEmpty(filePath)) return false;

            string outputDir = Path.GetDirectoryName(filePath);
            string sceneName = Path.GetFileNameWithoutExtension(filePath);
            _logPath = Path.Combine(outputDir, sceneName + "_export.log");

            // Initialize or clear log file
            try
            {
                if (File.Exists(_logPath)) File.Delete(_logPath);
            }
            catch { }

            Log("=================================================================");
            Log($"Starting Virtuart4D Datasmith Export (Hybrid Model) for: {doc.Title}");
            Log($"Target File: {filePath}");
            Log($"Merge Depth: {mergeMaxDepth} | Origin: ({originX}, {originY}, {originZ})");
            Log("=================================================================");

            try
            {
                Log("Step 1/2: Locating Epic Games Datasmith Exporter Plugin...");
                var pluginRecord = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin("ExporterPlugin.EpicGames");
                if (pluginRecord == null)
                {
                    Log("[ERROR] Epic Games Datasmith Exporter Plugin (ExporterPlugin.EpicGames) not found in Navisworks!");
                    return false;
                }

                Log("Loading Epic Games Datasmith Exporter Plugin...");
                if (!pluginRecord.IsLoaded)
                {
                    pluginRecord.LoadPlugin();
                }

                var plugin = pluginRecord.LoadedPlugin as Autodesk.Navisworks.Api.Plugins.AddInPlugin;
                if (plugin == null)
                {
                    Log("[ERROR] Failed to cast loaded plugin as AddInPlugin.");
                    return false;
                }

                Log("Executing Epic Games Datasmith Exporter silently...");
                
                // Format parameters with dot (.) as decimal separator for invariant culture parsing
                string originStr = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2}", originX, originY, originZ);
                string[] parameters = new string[]
                {
                    filePath,
                    $"Merge={mergeMaxDepth}",
                    $"Origin={originStr}"
                };

                // Epic plugin execution blocks synchronously until finished
                int exitCode = plugin.Execute(parameters);
                Log($"Epic Games Exporter finished. Exit Code: {exitCode}");

                if (!File.Exists(filePath))
                {
                    Log("[ERROR] The .udatasmith file was not created by the exporter.");
                    return false;
                }

                Log("Step 2/2: Post-processing generated .udatasmith XML to format metadata keys...");
                try
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.Load(filePath);
                    
                    var kvPs = xmlDoc.GetElementsByTagName("KeyValueProperty");
                    int modifiedKeys = 0;

                    foreach (XmlElement kvp in kvPs)
                    {
                        string name = kvp.GetAttribute("name");
                        if (name.Contains("*"))
                        {
                            kvp.SetAttribute("name", name.Replace("*", "."));
                            modifiedKeys++;
                        }
                    }

                    if (modifiedKeys > 0)
                    {
                        xmlDoc.Save(filePath);
                        Log($"Successfully updated {modifiedKeys} metadata keys to 'Category.Name' format.");
                    }
                    else
                    {
                        Log("No metadata key modifications were needed.");
                    }
                }
                catch (Exception xmlEx)
                {
                    Log($"[WARNING] Failed to post-process .udatasmith XML: {xmlEx.Message}");
                }

                Log("Datasmith Export completed successfully!");
                Log("=================================================================");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[CRITICAL ERROR] Export failed: {ex.Message}");
                Log($"Stack Trace: {ex.StackTrace}");
                return false;
            }
        }
    }
}
