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
            System.Collections.Generic.List<string[]> groupByProperties = null,
            bool useAutomation = false)
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

            if (groupByProperties != null && groupByProperties.Count > 0)
            {
                var propStrings = new System.Collections.Generic.List<string>();
                foreach (var propPair in groupByProperties)
                {
                    propStrings.Add($"Category: '{propPair[0]}', Property: '{propPair[1]}'");
                }
                Log($"Smart Merging Properties: {string.Join(" | ", propStrings)}");
            }
            else
            {
                Log("Smart Merging Properties: None (Standard Hierarchy Merging)");
            }
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

                Log($"Export Mode: {(useAutomation ? "Automation API (non-interactive)" : "Interactive Plugin")}");

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

                int exitCode;
                if (useAutomation)
                {
                    Log("Executing Epic Games Datasmith Exporter via Application.Automation.ExecuteAddInPlugin...");
                    Autodesk.Navisworks.Api.Application.Automation.ExecuteAddInPlugin(
                        "DatasmithNavisworksExporter.EpicGames",
                        parameters);
                    exitCode = 0;
                }
                else
                {
                    var plugin = pluginRecord.LoadedPlugin as Autodesk.Navisworks.Api.Plugins.AddInPlugin;
                    if (plugin == null)
                    {
                        Log("[ERROR] Failed to cast loaded plugin as AddInPlugin.");
                        return false;
                    }

                    Log("Executing Epic Games Datasmith Exporter silently/prompting if GUI...");
                    exitCode = plugin.Execute(parameters);
                }
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

                    // 2. Inject custom attributes from Navisworks into XML metadata
                    try
                    {
                        var itemMap = new System.Collections.Generic.Dictionary<string, ModelItem>(StringComparer.OrdinalIgnoreCase);
                        foreach (var model in doc.Models)
                        {
                            if (model.RootItem != null)
                            {
                                MapModelItemsByGuid(model.RootItem, itemMap);
                            }
                        }
                        Log($"Mapped {itemMap.Count} Navisworks ModelItems by InstanceGuid for metadata injection.");

                        var targetCategories = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            VirtuartSchema.CategoriaPrincipal,
                            "Virtuart_Sets",
                            "Custom"
                        };

                        if (groupByProperties != null)
                        {
                            foreach (var propPair in groupByProperties)
                            {
                                targetCategories.Add(propPair[0]);
                            }
                        }

                        // Get all Actor and ActorMesh nodes in the XML to find their corresponding Navisworks items
                        var xmlActors = xmlDoc.GetElementsByTagName("Actor");
                        var xmlActorMeshes = xmlDoc.GetElementsByTagName("ActorMesh");
                        var allActorsList = new System.Collections.Generic.List<XmlElement>();
                        foreach (XmlNode n in xmlActors) if (n is XmlElement el) allActorsList.Add(el);
                        foreach (XmlNode n in xmlActorMeshes) if (n is XmlElement el) allActorsList.Add(el);

                        // Build a map of reference -> MetaData XmlNode
                        var metaDataMap = new System.Collections.Generic.Dictionary<string, XmlNode>(StringComparer.OrdinalIgnoreCase);
                        var metaDataList = xmlDoc.GetElementsByTagName("MetaData");
                        var metaDataNodes = new System.Collections.Generic.List<XmlNode>();
                        foreach (XmlNode metaNode in metaDataList)
                        {
                            metaDataNodes.Add(metaNode);
                        }
                        foreach (XmlNode metaNode in metaDataNodes)
                        {
                            if (metaNode is XmlElement metaEl)
                            {
                                string reference = metaEl.GetAttribute("reference");
                                if (!string.IsNullOrEmpty(reference))
                                {
                                    metaDataMap[reference] = metaNode;
                                }
                            }
                        }

                        int propertiesInjected = 0;
                        foreach (var actorEl in allActorsList)
                        {
                            string actorName = actorEl.GetAttribute("name");
                            if (string.IsNullOrEmpty(actorName)) continue;

                            if (itemMap.TryGetValue(actorName, out ModelItem modelItem))
                            {
                                string metaRef = "Actor." + actorName;
                                XmlElement metaEl = null;
                                if (metaDataMap.TryGetValue(metaRef, out XmlNode existingMetaNode))
                                {
                                    metaEl = existingMetaNode as XmlElement;
                                }
                                else
                                {
                                    metaEl = xmlDoc.CreateElement("MetaData", xmlDoc.DocumentElement.NamespaceURI);
                                    metaEl.SetAttribute("name", actorName + "_DATA");
                                    metaEl.SetAttribute("reference", metaRef);
                                    xmlDoc.DocumentElement.AppendChild(metaEl);
                                    metaDataMap[metaRef] = metaEl;
                                }

                                // Collect existing keys to avoid duplicates
                                var existingKeys = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (XmlElement kvp in metaEl.GetElementsByTagName("KeyValueProperty"))
                                {
                                    string keyName = kvp.GetAttribute("name");
                                    if (!string.IsNullOrEmpty(keyName))
                                    {
                                        existingKeys.Add(keyName);
                                    }
                                }

                                // Traverse up the model item hierarchy and inject properties
                                ModelItem currentItem = modelItem;
                                while (currentItem != null)
                                {
                                    foreach (var category in currentItem.PropertyCategories)
                                    {
                                        string catName = category.DisplayName ?? category.Name;
                                        if (string.IsNullOrEmpty(catName)) continue;

                                        if (targetCategories.Contains(catName))
                                        {
                                            foreach (var prop in category.Properties)
                                            {
                                                string propName = prop.DisplayName ?? prop.Name;
                                                if (string.IsNullOrEmpty(propName)) continue;

                                                string propKey = $"{catName}.{propName}";
                                                if (!existingKeys.Contains(propKey))
                                                {
                                                    string val = FormatVariantData(prop.Value);
                                                    if (val != null)
                                                    {
                                                        XmlElement newKvp = xmlDoc.CreateElement("KeyValueProperty", xmlDoc.DocumentElement.NamespaceURI);
                                                        newKvp.SetAttribute("name", propKey);
                                                        newKvp.SetAttribute("type", "String");
                                                        newKvp.SetAttribute("val", val);
                                                        metaEl.AppendChild(newKvp);
                                                        existingKeys.Add(propKey);
                                                        propertiesInjected++;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    currentItem = currentItem.Parent;
                                }
                            }
                        }

                        Log($"Successfully injected {propertiesInjected} custom properties from Navisworks into .udatasmith XML.");
                    }
                    catch (Exception injectEx)
                    {
                        Log($"[WARNING] Failed to inject custom properties from Navisworks: {injectEx.Message}");
                    }

                    // 3. Perform property-based actor grouping if enabled
                    if (useCustomGrouping)
                    {
                        var propKeysLogged = new System.Collections.Generic.List<string>();
                        foreach (var propPair in groupByProperties)
                        {
                            propKeysLogged.Add($"Category: '{propPair[0]}', Property: '{propPair[1]}'");
                        }
                        Log($"Restructuring XML hierarchy by grouping actors using Smart Merging on: {string.Join(" | ", propKeysLogged)} (values are omitted)");
                        
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

                        // Map reference -> MetaData XmlNode for O(1) removals
                        var metaDataMap = new System.Collections.Generic.Dictionary<string, XmlNode>(StringComparer.OrdinalIgnoreCase);
                        foreach (XmlNode metaNode in metaDataNodes)
                        {
                            if (metaNode is XmlElement metaEl)
                            {
                                string reference = metaEl.GetAttribute("reference");
                                if (!string.IsNullOrEmpty(reference))
                                {
                                    metaDataMap[reference] = metaNode;
                                }
                            }
                        }

                        // Find all ActorMesh nodes in the XML hierarchy
                        var allActorMeshes = xmlDoc.GetElementsByTagName("ActorMesh");
                        var actorMeshList = new System.Collections.Generic.List<XmlNode>();
                        foreach (XmlNode meshNode in allActorMeshes)
                        {
                            actorMeshList.Add(meshNode);
                        }

                        Log($"Found {actorMeshList.Count} total ActorMesh node(s) to process.");

                        // A. Pre-calculate the anchor and group properties for all ActorMesh nodes
                        var meshInfos = new System.Collections.Generic.List<MeshGroupInfo>();
                        foreach (XmlNode meshNode in actorMeshList)
                        {
                            XmlNode anchor = FindAnchor(meshNode, mergeMaxDepth);
                            if (anchor == null) continue;

                            var groupProps = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            bool hasAnyValue = false;
                            var values = new System.Collections.Generic.List<string>();

                            foreach (var propPair in groupByProperties)
                            {
                                string cat = propPair[0];
                                string propName = propPair[1];
                                string val = GetNodePropertyValue(meshNode, cat, propName, actorMetadata);
                                groupProps[$"{cat}.{propName}"] = val;
                                values.Add(val);
                                if (!string.IsNullOrEmpty(val))
                                {
                                    hasAnyValue = true;
                                }
                            }

                            string groupKey = hasAnyValue ? string.Join("_", values) : null;

                            meshInfos.Add(new MeshGroupInfo
                            {
                                MeshNode = meshNode,
                                Anchor = anchor,
                                GroupKey = groupKey,
                                HasAnyValue = hasAnyValue,
                                GroupProps = groupProps
                            });
                        }

                        // B. Group the mesh infos by their anchor
                        var anchorGroups = new System.Collections.Generic.Dictionary<XmlNode, System.Collections.Generic.List<MeshGroupInfo>>();
                        foreach (var info in meshInfos)
                        {
                            if (!anchorGroups.TryGetValue(info.Anchor, out var list))
                            {
                                list = new System.Collections.Generic.List<MeshGroupInfo>();
                                anchorGroups[info.Anchor] = list;
                            }
                            list.Add(info);
                        }

                        Log($"Found {anchorGroups.Count} unique merge anchor(s) to process.");

                        int groupsCreated = 0;
                        int actorsMoved = 0;

                        // C. Process each anchor group
                        foreach (var pair in anchorGroups)
                        {
                            XmlNode anchorNode = pair.Key;
                            System.Collections.Generic.List<MeshGroupInfo> infos = pair.Value;

                            var keyToInfos = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<MeshGroupInfo>>(StringComparer.OrdinalIgnoreCase);
                            var normalInfos = new System.Collections.Generic.List<MeshGroupInfo>();

                            foreach (var info in infos)
                            {
                                if (info.HasAnyValue)
                                {
                                    if (!keyToInfos.TryGetValue(info.GroupKey, out var list))
                                    {
                                        list = new System.Collections.Generic.List<MeshGroupInfo>();
                                        keyToInfos[info.GroupKey] = list;
                                    }
                                    list.Add(info);
                                }
                                else
                                {
                                    normalInfos.Add(info);
                                }
                            }

                            // I. Process smart merged groups
                            foreach (var kvp in keyToInfos)
                            {
                                string groupKey = kvp.Key;
                                var groupInfos = kvp.Value;
                                var groupProps = groupInfos[0].GroupProps;

                                string parentName = (anchorNode is XmlElement anchorEl) ? anchorEl.GetAttribute("name") : "Node";
                                string parentLabel = (anchorNode is XmlElement anchorEl2) ? anchorEl2.GetAttribute("label") : parentName;
                                if (string.IsNullOrEmpty(parentName)) parentName = "Node";
                                if (string.IsNullOrEmpty(parentLabel)) parentLabel = parentName;
                                string groupActorName = "Group_" + parentName + "_" + CleanXmlAttribute(groupKey);
                                string groupActorLabel = parentLabel + "_" + groupKey;

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

                                XmlElement childrenElement = xmlDoc.CreateElement("children", xmlDoc.DocumentElement.NamespaceURI);
                                childrenElement.SetAttribute("visible", "true");
                                newActor.AppendChild(childrenElement);

                                // Move the ActorMesh nodes under the new group actor
                                foreach (var info in groupInfos)
                                {
                                    if (info.MeshNode.ParentNode != null)
                                    {
                                        info.MeshNode.ParentNode.RemoveChild(info.MeshNode);
                                    }
                                    childrenElement.AppendChild(info.MeshNode);
                                    actorsMoved++;
                                }

                                // Attach the new group actor as a sibling of anchorNode
                                XmlNode parentContainer = anchorNode.ParentNode;
                                if (parentContainer != null)
                                {
                                    parentContainer.AppendChild(newActor);
                                }
                                groupsCreated++;

                                // Create MetaData block for the group actor at the root level
                                XmlElement newMeta = xmlDoc.CreateElement("MetaData", xmlDoc.DocumentElement.NamespaceURI);
                                newMeta.SetAttribute("name", "Meta_" + groupActorName);
                                newMeta.SetAttribute("reference", "Actor." + groupActorName);

                                foreach (var propKvp in groupProps)
                                {
                                    XmlElement newKvp = xmlDoc.CreateElement("KeyValueProperty", xmlDoc.DocumentElement.NamespaceURI);
                                    newKvp.SetAttribute("name", propKvp.Key);
                                    newKvp.SetAttribute("type", "String");
                                    newKvp.SetAttribute("val", propKvp.Value);
                                    newMeta.AppendChild(newKvp);
                                }

                                xmlDoc.DocumentElement.AppendChild(newMeta);
                                metaDataMap["Actor." + groupActorName] = newMeta;
                            }

                            // II. Process normal merge meshes (those without the property)
                            if (normalInfos.Count > 0)
                            {
                                // Locate or create the Children element of the anchor node
                                XmlNode anchorChildren = null;
                                foreach (XmlNode child in anchorNode.ChildNodes)
                                {
                                    if (child.Name.Equals("children", StringComparison.OrdinalIgnoreCase))
                                    {
                                        anchorChildren = child;
                                        break;
                                    }
                                }
                                if (anchorChildren == null)
                                {
                                    XmlElement newChildren = xmlDoc.CreateElement("children", xmlDoc.DocumentElement.NamespaceURI);
                                    newChildren.SetAttribute("visible", "true");
                                    anchorChildren = newChildren;
                                    anchorNode.AppendChild(anchorChildren);
                                }

                                // Move all normal meshes directly under the anchor's Children element
                                foreach (var info in normalInfos)
                                {
                                    if (info.MeshNode.ParentNode != null)
                                    {
                                        info.MeshNode.ParentNode.RemoveChild(info.MeshNode);
                                    }
                                    anchorChildren.AppendChild(info.MeshNode);
                                    actorsMoved++;
                                }
                            }
                        }

                        // 4. Clean up all empty folders recursively across the whole document
                        Log("Running post-processing hierarchy cleanup...");
                        PostProcessCleanup(xmlDoc, metaDataMap);

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
                if (child.Name.Equals("children", StringComparison.OrdinalIgnoreCase))
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
                if (child.Name.Equals("children", StringComparison.OrdinalIgnoreCase))
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


        private static void RemoveEmptyActors(XmlNode node)
        {
            XmlNode childrenNode = null;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.Name.Equals("children", StringComparison.OrdinalIgnoreCase))
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

                        // An Actor/ActorMesh is empty if it has no geometry (it is "Actor") AND has no children with items
                        bool isGeo = child.Name == "ActorMesh";
                        bool hasSubChildren = false;
                        foreach (XmlNode sub in child.ChildNodes)
                        {
                            if (sub.Name.Equals("children", StringComparison.OrdinalIgnoreCase) && sub.HasChildNodes)
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

        private static string GetNodePropertyValue(
            XmlNode startNode, 
            string category, 
            string propertyName, 
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> actorMetadata)
        {
            string key = $"{category}.{propertyName}";
            XmlNode current = startNode;
            while (current != null)
            {
                if (current is XmlElement element)
                {
                    string name = element.GetAttribute("name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        if (actorMetadata.TryGetValue("Actor." + name, out var props))
                        {
                            if (props.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                            {
                                return val.Trim();
                            }
                        }
                    }
                }
                current = current.ParentNode;
            }
            return "";
        }

        private static string CleanXmlAttribute(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else sb.Append("_");
            }
            return sb.ToString();
        }

        private class MeshGroupInfo
        {
            public XmlNode MeshNode { get; set; }
            public XmlNode Anchor { get; set; }
            public string GroupKey { get; set; }
            public bool HasAnyValue { get; set; }
            public System.Collections.Generic.Dictionary<string, string> GroupProps { get; set; }
        }

        private static XmlNode FindAnchor(XmlNode meshNode, int mergeMaxDepth)
        {
            var ancestors = new System.Collections.Generic.List<XmlNode>();
            XmlNode current = meshNode.ParentNode;
            while (current != null && current != meshNode.OwnerDocument.DocumentElement)
            {
                if (current.Name == "Actor")
                {
                    ancestors.Add(current);
                }
                current = current.ParentNode;
            }
            
            ancestors.Reverse();
            
            if (ancestors.Count == 0)
            {
                return null;
            }
            
            if (ancestors.Count >= mergeMaxDepth)
            {
                return ancestors[mergeMaxDepth - 1];
            }
            else
            {
                return ancestors[ancestors.Count - 1];
            }
        }

        private static void CleanAllEmptyActors(XmlNode parentNode, System.Collections.Generic.Dictionary<string, XmlNode> metaDataMap)
        {
            XmlNode childrenNode = null;
            foreach (XmlNode child in parentNode.ChildNodes)
            {
                if (child.Name.Equals("children", StringComparison.OrdinalIgnoreCase))
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
                    if (child.Name == "Actor")
                    {
                        CleanAllEmptyActors(child, metaDataMap);

                        bool hasActiveChildren = false;
                        foreach (XmlNode sub in child.ChildNodes)
                        {
                            if (sub.Name.Equals("children", StringComparison.OrdinalIgnoreCase) && sub.HasChildNodes)
                            {
                                hasActiveChildren = true;
                                break;
                            }
                        }

                        if (!hasActiveChildren)
                        {
                            toRemove.Add(child);
                        }
                    }
                }

                foreach (XmlNode child in toRemove)
                {
                    childrenNode.RemoveChild(child);

                    if (child is XmlElement element)
                    {
                        string name = element.GetAttribute("name");
                        if (!string.IsNullOrEmpty(name) && metaDataMap.TryGetValue("Actor." + name, out var metaNode))
                        {
                            if (metaNode.ParentNode != null)
                            {
                                metaNode.ParentNode.RemoveChild(metaNode);
                            }
                            metaDataMap.Remove("Actor." + name);
                        }
                    }
                }

                if (!childrenNode.HasChildNodes)
                {
                    parentNode.RemoveChild(childrenNode);
                }
            }
        }

        private static void PostProcessCleanup(XmlDocument xmlDoc, System.Collections.Generic.Dictionary<string, XmlNode> metaDataMap)
        {
            var rootElements = new System.Collections.Generic.List<XmlNode>();
            foreach (XmlNode node in xmlDoc.DocumentElement.ChildNodes)
            {
                if (node.Name == "Actor")
                {
                    rootElements.Add(node);
                }
            }

            foreach (XmlNode rootActor in rootElements)
            {
                CleanAllEmptyActors(rootActor, metaDataMap);

                bool hasActiveChildren = false;
                foreach (XmlNode sub in rootActor.ChildNodes)
                {
                    if (sub.Name.Equals("children", StringComparison.OrdinalIgnoreCase) && sub.HasChildNodes)
                    {
                        hasActiveChildren = true;
                        break;
                    }
                }

                if (!hasActiveChildren && rootActor.ParentNode != null)
                {
                    rootActor.ParentNode.RemoveChild(rootActor);

                    if (rootActor is XmlElement element)
                    {
                        string name = element.GetAttribute("name");
                        if (!string.IsNullOrEmpty(name) && metaDataMap.TryGetValue("Actor." + name, out var metaNode))
                        {
                            if (metaNode.ParentNode != null)
                            {
                                metaNode.ParentNode.RemoveChild(metaNode);
                            }
                            metaDataMap.Remove("Actor." + name);
                        }
                    }
                }
            }
        }

        private static void MapModelItemsByGuid(ModelItem item, System.Collections.Generic.Dictionary<string, ModelItem> map)
        {
            if (item.InstanceGuid != Guid.Empty)
            {
                string key = item.InstanceGuid.ToString("N").ToLower();
                map[key] = item;
            }

            foreach (var child in item.Children)
            {
                MapModelItemsByGuid(child, map);
            }
        }

        private static string FormatVariantData(VariantData valor)
        {
            if (valor == null) return null;
            switch (valor.DataType)
            {
                case VariantDataType.Double:           return valor.ToDouble().ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                case VariantDataType.Int32:            return valor.ToInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
                case VariantDataType.Boolean:          return valor.ToBoolean() ? "True" : "False";
                case VariantDataType.DisplayString:    return valor.ToDisplayString();
                case VariantDataType.IdentifierString: return valor.ToIdentifierString();
                default:                               return valor.ToString();
            }
        }
    }
}
