using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;
using ComBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;
using COMApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace Virtuart4DNavisworks
{
    /// <summary>
    /// Performance-optimized service that coordinates the export of Navisworks scenes using the Datasmith C# Facade SDK.
    /// Employs a fast single-pass COM traversal and generates real-time logs to monitor export progress.
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
        /// Exports the active Navisworks document to a Datasmith file using a high-performance pipeline.
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
            Log($"Starting Virtuart4D Datasmith Export for: {doc.Title}");
            Log($"Target File: {filePath}");
            Log($"Merge Depth: {mergeMaxDepth} | Origin: ({originX}, {originY}, {originZ})");
            Log("=================================================================");

            var selection = doc.CurrentSelection.SelectedItems;
            var roots = new ModelItemCollection();

            if (selection != null && selection.Count > 0)
            {
                Log($"Exporting active selection ({selection.Count} root elements selected).");
                roots.AddRange(selection);
            }
            else
            {
                Log("Exporting entire visible models.");
                foreach (Model model in doc.Models)
                {
                    if (model.RootItem != null)
                    {
                        roots.Add(model.RootItem);
                    }
                }
            }

            if (roots.Count == 0)
            {
                Log("Error: No elements found to export.");
                return false;
            }

            try
            {
                Log("Initializing Datasmith Scene Facade...");
                using (var scene = new FDatasmithFacadeScene("Navisworks", "Autodesk", "Navisworks Manage", "2025"))
                {
                    scene.SetName(sceneName);
                    scene.SetOutputPath(outputDir);
                    scene.PreExport();

                    int meshesExported = 0;

                    Log("Pre-processing: Computing item heights for hierarchy merging...");
                    var heights = new Dictionary<ModelItem, int>();
                    foreach (ModelItem rootItem in roots)
                    {
                        ComputeHeightsRecursive(rootItem, heights);
                    }

                    Log("Step 1/3: Traversing lightweight .NET hierarchy to build actors dictionary...");
                    var actorsMap = new Dictionary<ModelItem, FDatasmithFacadeActor>();
                    int actorsCreated = 0;

                    foreach (ModelItem rootItem in roots)
                    {
                        BuildActorsHierarchyRecursive(rootItem, null, scene, actorsMap, ref actorsCreated, heights, mergeMaxDepth, originX, originY, originZ, ref meshesExported);
                    }
                    Log($"Step 1 Completed. Created {actorsCreated} actors in the hierarchy tree.");

                    Log("Step 2/3: Converting roots to COM selection and processing geometry fragments...");
                    
                    var leafCollection = new ModelItemCollection();
                    foreach (ModelItem root in roots)
                    {
                        foreach (ModelItem descendant in root.DescendantsAndSelf)
                        {
                            if (descendant.HasGeometry && !System.Linq.Enumerable.Any(descendant.Children) && !descendant.IsHidden && actorsMap.ContainsKey(descendant))
                            {
                                leafCollection.Add(descendant);
                            }
                        }
                    }
                    
                    Log($"Resolved {leafCollection.Count} concrete visible geometry leaf nodes from {roots.Count} roots.");
                    
                    COMApi.InwOpSelection oSel = ComBridge.ToInwOpSelection(leafCollection);
                    int processedPaths = 0;

                    foreach (COMApi.InwOaPath3 path in oSel.Paths())
                    {
                        processedPaths++;
                        ModelItem item = ComBridge.ToModelItem(path);
                        if (item == null) continue;

                        FDatasmithFacadeActor actor = null;
                        ModelItem current = item;
                        while (current != null)
                        {
                            if (actorsMap.TryGetValue(current, out actor))
                            {
                                break;
                            }
                            current = current.Parent;
                        }

                        if (actor == null)
                        {
                            continue;
                        }

                        if (actor is FDatasmithFacadeActorMesh actorMesh)
                        {
                            try
                            {
                                // Process and accumulate all fragments for this path safely
                                var allVertices = new List<float>();
                                var allNormals = new List<float>();
                                var allIndices = new List<int>();

                                COMApi.InwNodeFragsColl fragsColl = (COMApi.InwNodeFragsColl)path.Fragments();
                                foreach (COMApi.InwOaFragment3 frag in fragsColl)
                                {
                                    if (frag == null) continue;

                                    try
                                    {
                                        // Bake world matrix using frag.GetLocalToWorldMatrix()
                                        var callback = new DatasmithGeometryCallback(frag.GetLocalToWorldMatrix(), originX, originY, originZ);
                                        frag.GenerateSimplePrimitives(COMApi.nwEVertexProperty.eNORMAL, callback);

                                        if (callback.Vertices.Count > 0)
                                        {
                                            int vertOffset = allVertices.Count / 3;
                                            allVertices.AddRange(callback.Vertices);
                                            allNormals.AddRange(callback.Normals);

                                            foreach (int idx in callback.Indices)
                                            {
                                                allIndices.Add(vertOffset + idx);
                                            }
                                        }
                                    }
                                    catch (Exception fragEx)
                                    {
                                        Log($"[Fragment Error] Failed to process fragment on path: {fragEx.Message}");
                                    }
                                }

                                // Generate Datasmith mesh if geometry vertices are found
                                if (allVertices.Count > 0)
                                {
                                    using (var mesh = new FDatasmithFacadeMesh())
                                    {
                                        mesh.SetName(actorMesh.GetName());
                                        mesh.SetVerticesCount(allVertices.Count / 3);
                                        for (int i = 0; i < allVertices.Count / 3; i++)
                                        {
                                            mesh.SetVertex(i, allVertices[i * 3], allVertices[i * 3 + 1], allVertices[i * 3 + 2]);
                                            mesh.SetNormal(i, allNormals[i * 3], allNormals[i * 3 + 1], allNormals[i * 3 + 2]);
                                        }

                                        mesh.SetFacesCount(allIndices.Count / 3);
                                        for (int i = 0; i < allIndices.Count / 3; i++)
                                        {
                                            mesh.SetFace(i, allIndices[i * 3], allIndices[i * 3 + 1], allIndices[i * 3 + 2]);
                                        }

                                        var meshElement = new FDatasmithFacadeMeshElement(actorMesh.GetName());
                                        meshElement.SetLabel(actorMesh.GetLabel());

                                        bool meshSuccess = scene.ExportDatasmithMesh(meshElement, mesh);
                                        if (meshSuccess)
                                        {
                                            scene.AddMesh(meshElement);
                                            actorMesh.SetMesh(meshElement.GetName());
                                            meshesExported++;
                                        }
                                        else
                                        {
                                            Log($"[Mesh Error] FDatasmithFacadeScene::ExportDatasmithMesh returned false for: {meshElement.GetName()}");
                                            meshElement.Dispose();
                                        }
                                    }
                                }
                            }
                            catch (Exception pathEx)
                            {
                                Log($"[Path Error] Failed to process geometry on path: {pathEx.Message}");
                            }
                        }

                        if (processedPaths % 500 == 0)
                        {
                            Log($"Progress: Processed {processedPaths} nodes. Exported {meshesExported} meshes...");
                        }
                    }
                    Log($"Step 2 Completed. Total processed paths: {processedPaths}. Exported meshes: {meshesExported}.");

                    Log("Step 3/3: Saving Datasmith scene and finalizing export...");
                    bool success = scene.ExportScene(filePath);
                    scene.CleanUp();

                    Log($"Export completed successfully! Success status: {success}");
                    Log($"Log saved to: {_logPath}");
                    Log("=================================================================");
                    return success;
                }
            }
            catch (Exception ex)
            {
                Log($"[CRITICAL ERROR] Export failed: {ex.Message}");
                Log($"Stack Trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Recursively computes heights for model items to support Merge by Hierarchy.
        /// </summary>
        private static int ComputeHeightsRecursive(ModelItem item, Dictionary<ModelItem, int> heights)
        {
            if (item.IsHidden) return -1;

            int maxHeight = -1;
            bool isLeafGeometry = item.HasGeometry;
            bool hasVisibleChildren = false;

            foreach (ModelItem child in item.Children)
            {
                if (!child.IsHidden)
                {
                    hasVisibleChildren = true;
                    int childHeight = ComputeHeightsRecursive(child, heights);
                    if (childHeight > maxHeight)
                    {
                        maxHeight = childHeight;
                    }
                }
            }

            if (isLeafGeometry && !hasVisibleChildren)
            {
                heights[item] = 0;
                return 0;
            }

            if (maxHeight != -1)
            {
                int myHeight = maxHeight + 1;
                heights[item] = myHeight;
                return myHeight;
            }

            return -1;
        }

        /// <summary>
        /// Recursively traverses the lightweight .NET model items to construct the actor hierarchy tree.
        /// </summary>
        private static void BuildActorsHierarchyRecursive(
            ModelItem item, 
            FDatasmithFacadeActor parentActor, 
            FDatasmithFacadeScene scene,
            Dictionary<ModelItem, FDatasmithFacadeActor> actorsMap,
            ref int actorsCreated,
            Dictionary<ModelItem, int> heights,
            int mergeMaxDepth,
            double ox, double oy, double oz,
            ref int meshesExported)
        {
            if (item.IsHidden) return;

            // Check if this node qualifies as a Merge Root
            bool shouldMerge = mergeMaxDepth > 0 && 
                               heights.TryGetValue(item, out int height) && 
                               height <= mergeMaxDepth && 
                               (item.Parent == null || !heights.TryGetValue(item.Parent, out int parentHeight) || parentHeight > mergeMaxDepth);

            if (shouldMerge)
            {
                string guidStr = item.InstanceGuid != Guid.Empty ? item.InstanceGuid.ToString() : item.GetHashCode().ToString();
                string cleanName = "Node_" + guidStr.Replace("{", "").Replace("}", "").Replace("-", "_");

                var actorMesh = new FDatasmithFacadeActorMesh(cleanName);
                actorMesh.SetLabel(item.DisplayName ?? item.ClassDisplayName ?? "Element");
                actorsMap[item] = actorMesh;
                actorsCreated++;

                // Extract metadata properties and associate with this actor
                ExportProperties(item, actorMesh, scene);

                if (parentActor != null)
                {
                    parentActor.AddChild(actorMesh);
                }
                else
                {
                    scene.AddActor(actorMesh);
                }

                // Process and merge all descendant geometries
                ExtractAndMergeGeometry(item, actorMesh, scene, ox, oy, oz, ref meshesExported);

                // Stop tree traversal recursion here
                return;
            }

            string cleanGuid = item.InstanceGuid != Guid.Empty ? item.InstanceGuid.ToString() : item.GetHashCode().ToString();
            string standardName = "Node_" + cleanGuid.Replace("{", "").Replace("}", "").Replace("-", "_");

            FDatasmithFacadeActor currentActor = null;

            if (item.HasGeometry && !System.Linq.Enumerable.Any(item.Children))
            {
                currentActor = new FDatasmithFacadeActorMesh(standardName);
            }
            else
            {
                currentActor = new FDatasmithFacadeActor(standardName);
            }

            currentActor.SetLabel(item.DisplayName ?? item.ClassDisplayName ?? "Element");
            actorsMap[item] = currentActor;
            actorsCreated++;

            // Extract metadata properties and associate with this actor
            ExportProperties(item, currentActor, scene);

            if (parentActor != null)
            {
                parentActor.AddChild(currentActor);
            }
            else
            {
                scene.AddActor(currentActor);
            }

            foreach (ModelItem child in item.Children)
            {
                BuildActorsHierarchyRecursive(child, currentActor, scene, actorsMap, ref actorsCreated, heights, mergeMaxDepth, ox, oy, oz, ref meshesExported);
            }
        }

        /// <summary>
        /// Gathers all descendant geometries and merges them into a single FDatasmithFacadeMesh.
        /// </summary>
        private static void ExtractAndMergeGeometry(
            ModelItem mergeRoot, 
            FDatasmithFacadeActorMesh actorMesh, 
            FDatasmithFacadeScene scene,
            double ox, double oy, double oz,
            ref int meshesExported)
        {
            var allVertices = new List<float>();
            var allNormals = new List<float>();
            var allIndices = new List<int>();

            // Collect all leaf geometry descendants (including self)
            var leaves = new List<ModelItem>();
            CollectLeafGeometryDescendants(mergeRoot, leaves);

            if (leaves.Count == 0) return;

            // Convert to COMApi selection to access paths
            var leafCollection = new ModelItemCollection();
            leafCollection.AddRange(leaves);
            COMApi.InwOpSelection oSel = ComBridge.ToInwOpSelection(leafCollection);

            foreach (COMApi.InwOaPath3 path in oSel.Paths())
            {
                try
                {
                    COMApi.InwNodeFragsColl fragsColl = (COMApi.InwNodeFragsColl)path.Fragments();
                    foreach (COMApi.InwOaFragment3 frag in fragsColl)
                    {
                        if (frag == null) continue;

                        try
                        {
                            var callback = new DatasmithGeometryCallback(frag.GetLocalToWorldMatrix(), ox, oy, oz);
                            frag.GenerateSimplePrimitives(COMApi.nwEVertexProperty.eNORMAL, callback);

                            if (callback.Vertices.Count > 0)
                            {
                                int vertOffset = allVertices.Count / 3;
                                allVertices.AddRange(callback.Vertices);
                                allNormals.AddRange(callback.Normals);

                                foreach (int idx in callback.Indices)
                                {
                                    allIndices.Add(vertOffset + idx);
                                }
                            }
                        }
                        catch (Exception fragEx)
                        {
                            Log($"[Fragment Error] Failed to process fragment on path inside merged actor: {fragEx.Message}");
                        }
                    }
                }
                catch (Exception pathEx)
                {
                    Log($"[Path Error] Failed to process path inside merged actor: {pathEx.Message}");
                }
            }

            if (allVertices.Count > 0)
            {
                using (var mesh = new FDatasmithFacadeMesh())
                {
                    mesh.SetName(actorMesh.GetName());
                    mesh.SetVerticesCount(allVertices.Count / 3);
                    for (int i = 0; i < allVertices.Count / 3; i++)
                    {
                        mesh.SetVertex(i, allVertices[i * 3], allVertices[i * 3 + 1], allVertices[i * 3 + 2]);
                        mesh.SetNormal(i, allNormals[i * 3], allNormals[i * 3 + 1], allNormals[i * 3 + 2]);
                    }

                    mesh.SetFacesCount(allIndices.Count / 3);
                    for (int i = 0; i < allIndices.Count / 3; i++)
                    {
                        mesh.SetFace(i, allIndices[i * 3], allIndices[i * 3 + 1], allIndices[i * 3 + 2]);
                    }

                    var meshElement = new FDatasmithFacadeMeshElement(actorMesh.GetName());
                    meshElement.SetLabel(actorMesh.GetLabel());

                    bool meshSuccess = scene.ExportDatasmithMesh(meshElement, mesh);
                    if (meshSuccess)
                    {
                        scene.AddMesh(meshElement);
                        actorMesh.SetMesh(meshElement.GetName());
                        meshesExported++;
                    }
                    else
                    {
                        Log($"[Mesh Error] FDatasmithFacadeScene::ExportDatasmithMesh returned false for merged actor: {meshElement.GetName()}");
                        meshElement.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Recursively collects leaf geometry items from a model node.
        /// </summary>
        private static void CollectLeafGeometryDescendants(ModelItem item, List<ModelItem> leaves)
        {
            if (item.IsHidden) return;

            if (item.HasGeometry && !System.Linq.Enumerable.Any(item.Children))
            {
                leaves.Add(item);
                return;
            }

            foreach (ModelItem child in item.Children)
            {
                CollectLeafGeometryDescendants(child, leaves);
            }
        }

        /// <summary>
        /// Extracts standard and custom property categories and maps them into Datasmith Metadata.
        /// </summary>
        private static void ExportProperties(ModelItem item, FDatasmithFacadeActor actor, FDatasmithFacadeScene scene)
        {
            var metaData = new FDatasmithFacadeMetaData("Meta_" + actor.GetName());
            metaData.SetAssociatedElement(actor);

            bool hasProperties = false;

            foreach (PropertyCategory category in item.PropertyCategories)
            {
                string catName = category.DisplayName ?? category.Name;
                if (string.IsNullOrEmpty(catName)) continue;

                foreach (DataProperty prop in category.Properties)
                {
                    string propName = prop.DisplayName ?? prop.Name;
                    if (string.IsNullOrEmpty(propName)) continue;

                    string key = $"{catName}.{propName}";
                    string val = prop.Value?.ToString();

                    if (val != null)
                    {
                        metaData.AddPropertyString(key, val);
                        hasProperties = true;
                    }
                }
            }

            if (hasProperties)
            {
                scene.AddMetaData(metaData);
            }
            else
            {
                metaData.Dispose();
            }
        }
    }
}
