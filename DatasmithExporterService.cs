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
        private static readonly System.Collections.Generic.List<string> _logBuffer = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Writes a timestamped message to both the debug console and the export log file.
        /// </summary>
        private static void Log(string message)
        {
            try
            {
                string logMsg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                System.Diagnostics.Debug.WriteLine(logMsg);
                lock (_logBuffer)
                {
                    _logBuffer.Add(logMsg);
                }
                if (!string.IsNullOrEmpty(_logPath))
                {
                    File.AppendAllText(_logPath, logMsg + Environment.NewLine);
                }
            }
            catch { }
        }

        /// <summary>
        /// Exports the active Navisworks document to a Datasmith file using the hybrid delegation pipeline.
        /// </summary>
        /// <param name="doc">The active Navisworks Document.</param>
        /// <param name="filePath">Target .udatasmith file path (if null or empty, Epic's dialog will prompt).</param>
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
            double originZ = 0,
            string originSelectionType = "Default / Manual",
            string selectedElementName = "None",
            string selectedElementId = "None")
        {
            if (doc == null) return false;

            lock (_logBuffer)
            {
                _logBuffer.Clear();
            }

            // If filePath is empty or null, we suggest the default name in the doc's folder
            string defaultName = "";
            string defaultDir = "";
            if (!string.IsNullOrEmpty(doc.FileName))
            {
                defaultName = Path.GetFileNameWithoutExtension(doc.FileName) + ".udatasmith";
                defaultDir = Path.GetDirectoryName(doc.FileName);
            }
            else
            {
                defaultName = "Export.udatasmith";
                defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            string exportFilePath = string.IsNullOrEmpty(filePath) ? Path.Combine(defaultDir, defaultName) : filePath;
            
            string outputDir = Path.GetDirectoryName(exportFilePath);
            string sceneName = Path.GetFileNameWithoutExtension(exportFilePath);
            
            // Keep logPath as null during execution so we buffer only in memory and do not touch disk files initially
            _logPath = null;

            Log("=================================================================");
            Log($"Starting Virtuart4D Datasmith Export (Hybrid Model) for: {doc.Title}");
            Log($"Target File (Default/User): {exportFilePath}");
            Log($"Merge Depth: {mergeMaxDepth} | Origin: ({originX.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, {originY.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, {originZ.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)})");
            Log($"Origin Selection Type: {originSelectionType}");
            Log($"Selected Element Name: {selectedElementName}");
            Log($"Selected Element Unique ID: {selectedElementId}");
            Log("=================================================================");

            try
            {
                Log("Step 1/2: Locating Epic Games Datasmith Exporter Plugin...");
                var pluginRecord = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin("DatasmithNavisworksExporter.EpicGames");
                if (pluginRecord == null)
                {
                    Log("[ERROR] Epic Games Datasmith Exporter Plugin (DatasmithNavisworksExporter.EpicGames) not found in Navisworks!");
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

                Log("Executing Epic Games Datasmith Exporter silently/prompting if GUI...");
                
                // Format parameters with dot (.) as decimal separator for invariant culture parsing
                string originStr = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2}", originX, originY, originZ);
                string[] parameters = new string[]
                {
                    exportFilePath,
                    $"Merge={mergeMaxDepth}",
                    $"Origin={originStr}"
                };

                // Epic plugin execution blocks synchronously until finished
                int exitCode = plugin.Execute(parameters);
                Log($"Epic Games Exporter finished. Exit Code: {exitCode}");

                // Locate the actually exported file path
                string resolvedFilePath = null;
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    resolvedFilePath = filePath;
                }
                else
                {
                    // Scan current working directory and other common directories for the newly created udatasmith file
                    resolvedFilePath = FindMostRecentDatasmithFile(defaultDir);
                }

                if (string.IsNullOrEmpty(resolvedFilePath) || !File.Exists(resolvedFilePath))
                {
                    Log("[ERROR] The .udatasmith file was not found after export.");
                    // Fallback to write failure logs to default file name
                    string fallbackLogPath = Path.Combine(outputDir, sceneName + "_export.log");
                    try
                    {
                        File.WriteAllLines(fallbackLogPath, _logBuffer.ToArray());
                    }
                    catch {}
                    return false;
                }

                Log($"Located exported file at: {resolvedFilePath}");

                // Dynamic Log Path Renaming & Overwriting based on the actually exported file name
                string finalOutputDir = Path.GetDirectoryName(resolvedFilePath);
                string finalSceneName = Path.GetFileNameWithoutExtension(resolvedFilePath);
                string finalLogPath = Path.Combine(finalOutputDir, finalSceneName + "_export.log");

                _logPath = finalLogPath;

                // Overwrite final log file by deleting if it exists
                try
                {
                    if (File.Exists(finalLogPath)) File.Delete(finalLogPath);
                }
                catch { }

                // Write all buffered lines so far to the final log file
                try
                {
                    lock (_logBuffer)
                    {
                        File.WriteAllLines(_logPath, _logBuffer.ToArray());
                    }
                }
                catch { }

                Log("Step 2/2: Post-processing generated .udatasmith XML to format metadata keys...");
                try
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.Load(resolvedFilePath);
                    
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
                        xmlDoc.Save(resolvedFilePath);
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
                
                // If log path was never set (i.e. failed before dialog resolved it),
                // write the buffer to the default log path so the failure is recorded.
                if (string.IsNullOrEmpty(_logPath))
                {
                    string fallbackLogPath = Path.Combine(outputDir, sceneName + "_export.log");
                    try
                    {
                        File.WriteAllLines(fallbackLogPath, _logBuffer.ToArray());
                    }
                    catch {}
                }
                return false;
            }
        }

        /// <summary>
        /// Searches common folders and current working directory for the most recently created .udatasmith file.
        /// </summary>
        private static string FindMostRecentDatasmithFile(string defaultDir)
        {
            try
            {
                var dirsToSearch = new System.Collections.Generic.List<string>();
                
                // 1. Current Working Directory is the most likely since FileDialog changes the CWD of the process
                string cwd = Directory.GetCurrentDirectory();
                if (Directory.Exists(cwd)) dirsToSearch.Add(cwd);
                
                // 2. Default Active Document Directory
                if (!string.IsNullOrEmpty(defaultDir) && Directory.Exists(defaultDir) && !dirsToSearch.Contains(defaultDir))
                {
                    dirsToSearch.Add(defaultDir);
                }

                // 3. Documents Folder
                string myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (Directory.Exists(myDocs) && !dirsToSearch.Contains(myDocs))
                {
                    dirsToSearch.Add(myDocs);
                }

                // 4. Desktop
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (Directory.Exists(desktop) && !dirsToSearch.Contains(desktop))
                {
                    dirsToSearch.Add(desktop);
                }

                string bestFile = null;
                DateTime bestTime = DateTime.MinValue;

                foreach (string dir in dirsToSearch)
                {
                    try
                    {
                        foreach (string file in Directory.GetFiles(dir, "*.udatasmith"))
                        {
                            var fi = new FileInfo(file);
                            // Must have been modified in the last 40 seconds
                            if (fi.LastWriteTime > bestTime && fi.LastWriteTime > DateTime.Now.AddSeconds(-40))
                            {
                                bestTime = fi.LastWriteTime;
                                bestFile = file;
                            }
                        }
                    }
                    catch { }
                }

                return bestFile;
            }
            catch
            {
                return null;
            }
        }
    }
}
