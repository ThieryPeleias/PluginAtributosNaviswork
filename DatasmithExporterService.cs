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
            string selectedElementId = "None",
            System.Collections.Generic.List<string[]> groupByProperties = null)
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
                
                bool useCustomGrouping = groupByProperties != null && groupByProperties.Count > 0 && mergeMaxDepth > 0;
                int exporterMergeDepth = useCustomGrouping ? 0 : mergeMaxDepth;

                if (useCustomGrouping)
                {
                    Log($"Smart grouping enabled on {groupByProperties.Count} properties. Delegating to Epic Games plugin with Merge=0 (leaves separate) and will restructure XML to Merge Depth {mergeMaxDepth} in post-processing.");
                }

                // Format parameters with dot (.) as decimal separator for invariant culture parsing
                string originStr = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2}", originX, originY, originZ);
                string[] parameters = new string[]
                {
                    exportFilePath,
                    $"Merge={exporterMergeDepth}",
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

                Log("Step 2/2: Post-processing generated .udatasmith XML...");
                try
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.Load(resolvedFilePath);
                    
                    // 1. Normalize metadata property keys (replace * with .)
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
                        Log($"Successfully updated {modifiedKeys} metadata keys to 'Category.Name' format.");
                    }

                    // 2. Perform property-based actor grouping if enabled
                    if (useCustomGrouping)
                    {
                        Log("Restructuring XML hierarchy by grouping actors with matching custom property values...");
                        
                        // Map ActorName -> (Category.Property -> Value)
                        var actorMetadata = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                        
                        var metaDataList = xmlDoc.GetElementsByTagName("MetaData");
                        var metaDataNodes = new System.Collections.Generic.List<XmlNode>();
                        foreach (XmlNode metaNode in metaDataList)
                        {
                            metaDataNodes.Add(metaNode);
                        }

                        foreach (XmlElement meta in metaDataNodes)
                        {
                            string reference = meta.GetAttribute("reference");
                            if (string.IsNullOrEmpty(reference)) continue;

                            var props = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            actorMetadata[reference] = props;

                            var kvpList = meta.GetElementsByTagName("KeyValueProperty");
                            foreach (XmlElement kvp in kvpList)
                            {
                                string propName = kvp.GetAttribute("name");
                                string propVal = kvp.GetAttribute("val");
                                if (!string.IsNullOrEmpty(propName))
                                {
                                    props[propName] = propVal ?? "";
                                }
                            }
                        }

                        // Find all nodes at depth mergeMaxDepth in the XML hierarchy
                        var nodesAtDepth = new System.Collections.Generic.List<XmlNode>();
                        var rootElements = xmlDoc.DocumentElement.ChildNodes;
                        foreach (XmlNode node in rootElements)
                        {
                            if (node.Name == "Actor" || node.Name == "ActorMesh")
                            {
                                FindNodesAtDepth(node, 1, mergeMaxDepth, nodesAtDepth);
                            }
                        }

                        Log($"Found {nodesAtDepth.Count} tree node(s) at Merge Depth {mergeMaxDepth} to process.");

                        int groupsCreated = 0;
                        int actorsMoved = 0;

                        foreach (XmlNode parentNode in nodesAtDepth)
                        {
                            // Collect all descendant ActorMesh elements (leaves with geometry)
                            var descendantActorMeshes = new System.Collections.Generic.List<XmlNode>();
                            CollectActorMeshDescendants(parentNode, descendantActorMeshes);

                            if (descendantActorMeshes.Count == 0) continue;

                            // Group descendant ActorMesh nodes by their combined property key values
                            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<XmlNode>>(StringComparer.OrdinalIgnoreCase);
                            var groupPropertiesMap = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                            foreach (XmlNode meshNode in descendantActorMeshes)
                            {
                                string actorName = meshNode.Attributes["name"]?.Value;
                                if (string.IsNullOrEmpty(actorName)) continue;

                                System.Collections.Generic.Dictionary<string, string> groupProps;
                                string key = GetGroupKey(actorName, groupByProperties, actorMetadata, out groupProps);
                                
                                // Check if the key is effectively empty (all selected property values are empty)
                                bool isKeyEmpty = true;
                                foreach (var val in groupProps.Values)
                                {
                                    if (!string.IsNullOrEmpty(val))
                                    {
                                        isKeyEmpty = false;
                                        break;
                                    }
                                }

                                if (isKeyEmpty)
                                {
                                    // Keep separate, do not group
                                    continue;
                                }

                                if (!groups.TryGetValue(key, out var list))
                                {
                                    list = new System.Collections.Generic.List<XmlNode>();
                                    groups[key] = list;
                                    groupPropertiesMap[key] = groupProps;
                                }
                                list.Add(meshNode);
                            }

                            // Create new group actor for each unique property value combination
                            foreach (var kvp in groups)
                            {
                                string groupKey = kvp.Key;
                                var groupNodes = kvp.Value;
                                var groupProps = groupPropertiesMap[groupKey];

                                string parentName = parentNode.Attributes["name"]?.Value ?? "Node";
                                string groupActorName = "Group_" + parentName + "_" + Math.Abs(groupKey.GetHashCode());
                                string groupActorLabel = "Group - " + groupKey;

                                XmlElement newActor = xmlDoc.CreateElement("Actor", xmlDoc.DocumentElement.NamespaceURI);
                                newActor.SetAttribute("name", groupActorName);
                                newActor.SetAttribute("label", groupActorLabel);

                                XmlElement transform = xmlDoc.CreateElement("Transform", xmlDoc.DocumentElement.NamespaceURI);
                                transform.SetAttribute("tx", "0");
                                transform.SetAttribute("ty", "0");
                                transform.SetAttribute("tz", "0");
                                transform.SetAttribute("sx", "1");
                                transform.SetAttribute("sy", "1");
                                transform.SetAttribute("sz", "1");
                                transform.SetAttribute("qx", "0");
                                transform.SetAttribute("qy", "0");
                                transform.SetAttribute("qz", "0");
                                transform.SetAttribute("qw", "1");
                                newActor.AppendChild(transform);

                                XmlElement childrenElement = xmlDoc.CreateElement("Children", xmlDoc.DocumentElement.NamespaceURI);
                                newActor.AppendChild(childrenElement);

                                // Move ActorMesh elements under the new group actor
                                foreach (XmlNode node in groupNodes)
                                {
                                    if (node.ParentNode != null)
                                    {
                                        node.ParentNode.RemoveChild(node);
                                    }
                                    childrenElement.AppendChild(node);
                                    actorsMoved++;
                                }

                                // Attach the new group actor to parentNode's Children node
                                XmlNode parentChildrenNode = null;
                                foreach (XmlNode child in parentNode.ChildNodes)
                                {
                                    if (child.Name == "Children")
                                    {
                                        parentChildrenNode = child;
                                        break;
                                    }
                                }

                                if (parentChildrenNode == null)
                                {
                                    parentChildrenNode = xmlDoc.CreateElement("Children", xmlDoc.DocumentElement.NamespaceURI);
                                    parentNode.AppendChild(parentChildrenNode);
                                }

                                parentChildrenNode.AppendChild(newActor);
                                groupsCreated++;

                                // Create MetaData block for the group actor at the root level
                                XmlElement newMeta = xmlDoc.CreateElement("MetaData", xmlDoc.DocumentElement.NamespaceURI);
                                newMeta.SetAttribute("name", "Meta_" + groupActorName);
                                newMeta.SetAttribute("reference", groupActorName);

                                foreach (var propKvp in groupProps)
                                {
                                    XmlElement newKvp = xmlDoc.CreateElement("KeyValueProperty", xmlDoc.DocumentElement.NamespaceURI);
                                    newKvp.SetAttribute("name", propKvp.Key);
                                    newKvp.SetAttribute("type", "String");
                                    newKvp.SetAttribute("val", propKvp.Value);
                                    newMeta.AppendChild(newKvp);
                                }

                                xmlDoc.DocumentElement.AppendChild(newMeta);
                            }

                            // Clean up empty folders/actors that are now children of parentNode
                            RemoveEmptyActors(parentNode);
                        }

                        Log($"Restructuring complete. Created {groupsCreated} group actor(s) and regrouped {actorsMoved} item(s) by properties.");
                    }

                    xmlDoc.Save(resolvedFilePath);
                    Log("Successfully saved restructured .udatasmith XML file.");
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

        private static void FindNodesAtDepth(XmlNode node, int currentDepth, int targetDepth, System.Collections.Generic.List<XmlNode> result)
        {
            if (currentDepth == targetDepth)
            {
                result.Add(node);
                return;
            }

            XmlNode childrenNode = null;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.Name == "Children")
                {
                    childrenNode = child;
                    break;
                }
            }

            if (childrenNode != null)
            {
                foreach (XmlNode child in childrenNode.ChildNodes)
                {
                    if (child.Name == "Actor" || child.Name == "ActorMesh")
                    {
                        FindNodesAtDepth(child, currentDepth + 1, targetDepth, result);
                    }
                }
            }
        }

        private static void CollectActorMeshDescendants(XmlNode node, System.Collections.Generic.List<XmlNode> list)
        {
            if (node.Name == "ActorMesh")
            {
                list.Add(node);
            }

            XmlNode childrenNode = null;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.Name == "Children")
                {
                    childrenNode = child;
                    break;
                }
            }

            if (childrenNode != null)
            {
                foreach (XmlNode child in childrenNode.ChildNodes)
                {
                    CollectActorMeshDescendants(child, list);
                }
            }
        }

        private static string GetGroupKey(
            string actorName, 
            System.Collections.Generic.List<string[]> groupByProperties, 
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> actorMetadata, 
            out System.Collections.Generic.Dictionary<string, string> groupProps)
        {
            groupProps = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var values = new System.Collections.Generic.List<string>();
            
            actorMetadata.TryGetValue(actorName, out var props);

            foreach (var propPair in groupByProperties)
            {
                string cat = propPair[0];
                string propName = propPair[1];
                string key = $"{cat}.{propName}";
                string val = "";
                if (props != null && props.TryGetValue(key, out var v))
                {
                    val = v ?? "";
                }
                groupProps[key] = val;
                values.Add(val);
            }

            return string.Join(" | ", values);
        }

        private static void RemoveEmptyActors(XmlNode node)
        {
            XmlNode childrenNode = null;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.Name == "Children")
                {
                    childrenNode = child;
                    break;
                }
            }

            if (childrenNode != null)
            {
                var toRemove = new System.Collections.Generic.List<XmlNode>();
                foreach (XmlNode child in childrenNode.ChildNodes)
                {
                    if (child.Name == "Actor" || child.Name == "ActorMesh")
                    {
                        RemoveEmptyActors(child);

                        // An Actor/ActorMesh is empty if it has no geometry (it is "Actor") AND has no Children with items
                        bool isGeo = child.Name == "ActorMesh";
                        bool hasSubChildren = false;
                        foreach (XmlNode sub in child.ChildNodes)
                        {
                            if (sub.Name == "Children" && sub.HasChildNodes)
                            {
                                hasSubChildren = true;
                                break;
                            }
                        }

                        if (!isGeo && !hasSubChildren)
                        {
                            toRemove.Add(child);
                        }
                    }
                }

                foreach (XmlNode child in toRemove)
                {
                    childrenNode.RemoveChild(child);
                }

                if (!childrenNode.HasChildNodes)
                {
                    node.RemoveChild(childrenNode);
                }
            }
        }
    }
}
