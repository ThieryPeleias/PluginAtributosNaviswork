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
        /// <returns>True if export was successful, false otherwise.</returns>
        public static bool ExportActiveDocument(Document doc, string filePath)
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

                    Log("Step 1/3: Traversing lightweight .NET hierarchy to build actors dictionary...");
                    var actorsMap = new Dictionary<ModelItem, FDatasmithFacadeActor>();
                    int actorsCreated = 0;

                    foreach (ModelItem rootItem in roots)
                    {
                        BuildActorsHierarchyRecursive(rootItem, null, scene, actorsMap, ref actorsCreated);
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
                    int meshesExported = 0;
                    var exportedMeshesCache = new Dictionary<string, string>();

                    foreach (COMApi.InwOaPath3 path in oSel.Paths())
                    {
                        processedPaths++;
                        ModelItem item = ComBridge.ToModelItem(path);
                        if (item == null) continue;

                        FDatasmithFacadeActor parentActor = null;
                        ModelItem current = item;
                        while (current != null)
                        {
                            if (actorsMap.TryGetValue(current, out parentActor))
                            {
                                break;
                            }
                            current = current.Parent;
                        }

                        if (parentActor == null)
                        {
                            continue;
                        }

                        try
                        {
                            dynamic fragsColl = path.Fragments();
                            int fragsCount = fragsColl.Count;

                            if (fragsCount > 1)
                            {
                                Log($"[Diag] Actor '{parentActor.GetName()}' has {fragsCount} fragments.");
                            }

                            for (int f = 1; f <= fragsCount; f++)
                            {
                                try
                                {
                                    dynamic frag = fragsColl.Item(f);
                                    if (frag == null) continue;

                                    // Extract matrix and convert to Unreal Z-Up Left-Handed Row-Major format
                                    var rawMatrix = DatasmithGeometryCallback.ExtractMatrix(frag.GetLocalToWorldMatrix());
                                    var unrealMatrix = ConvertMatrixToUnreal(rawMatrix);

                                    // Extract geometry in Local Coordinate Space (LCS) by passing null matrix
                                    var callback = new DatasmithGeometryCallback(null);
                                    frag.GenerateSimplePrimitives(COMApi.nwEVertexProperty.eNORMAL, callback);

                                    if (callback.Vertices.Count > 0)
                                    {
                                        string meshName = null;
                                        string geoHash = GetGeometryHash(callback.Vertices, callback.Indices);

                                        if (!exportedMeshesCache.TryGetValue(geoHash, out meshName))
                                        {
                                            // Create and export new unique static mesh
                                            using (var mesh = new FDatasmithFacadeMesh())
                                            {
                                                string newMeshName = parentActor.GetName() + "_Mesh_" + f;
                                                mesh.SetName(newMeshName);
                                                mesh.SetVerticesCount(callback.Vertices.Count / 3);
                                                for (int i = 0; i < callback.Vertices.Count / 3; i++)
                                                {
                                                    mesh.SetVertex(i, callback.Vertices[i * 3], callback.Vertices[i * 3 + 1], callback.Vertices[i * 3 + 2]);
                                                    mesh.SetNormal(i, callback.Normals[i * 3], callback.Normals[i * 3 + 1], callback.Normals[i * 3 + 2]);
                                                }

                                                mesh.SetFacesCount(callback.Indices.Count / 3);
                                                for (int i = 0; i < callback.Indices.Count / 3; i++)
                                                {
                                                    mesh.SetFace(i, callback.Indices[i * 3], callback.Indices[i * 3 + 1], callback.Indices[i * 3 + 2]);
                                                }

                                                var meshElement = new FDatasmithFacadeMeshElement(newMeshName + "Element");
                                                meshElement.SetLabel(parentActor.GetLabel() + "_Mesh_" + f);

                                                bool meshSuccess = scene.ExportDatasmithMesh(meshElement, mesh);
                                                if (meshSuccess)
                                                {
                                                    scene.AddMesh(meshElement);
                                                    meshName = meshElement.GetName();
                                                    exportedMeshesCache[geoHash] = meshName;
                                                    meshesExported++;
                                                }
                                                else
                                                {
                                                    Log($"[Mesh Error] FDatasmithFacadeScene::ExportDatasmithMesh returned false for: {meshElement.GetName()}");
                                                    meshElement.Dispose();
                                                }
                                            }
                                        }

                                        if (meshName != null)
                                        {
                                            // Create unique FDatasmithFacadeActorMesh for this fragment instance
                                            string actorMeshName = parentActor.GetName() + "_Actor_" + f;
                                            var actorMesh = new FDatasmithFacadeActorMesh(actorMeshName);
                                            actorMesh.SetLabel(parentActor.GetLabel() + "_" + f);
                                            actorMesh.SetMesh(meshName);
                                            actorMesh.SetWorldTransform(unrealMatrix, true);

                                            parentActor.AddChild(actorMesh);
                                        }
                                    }
                                }
                                catch (Exception fragEx)
                                {
                                    Log($"[Fragment Error] Failed to process fragment {f} on path: {fragEx.Message}");
                                }
                            }
                        }
                        catch (Exception pathEx)
                        {
                            Log($"[Path Error] Failed to process geometry on path: {pathEx.Message}");
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
        /// Recursively traverses the lightweight .NET model items to construct the actor hierarchy tree.
        /// </summary>
        private static void BuildActorsHierarchyRecursive(
            ModelItem item, 
            FDatasmithFacadeActor parentActor, 
            FDatasmithFacadeScene scene,
            Dictionary<ModelItem, FDatasmithFacadeActor> actorsMap,
            ref int actorsCreated)
        {
            if (item.IsHidden) return;

            string guidStr = item.InstanceGuid != Guid.Empty ? item.InstanceGuid.ToString() : item.GetHashCode().ToString();
            string cleanName = "Node_" + guidStr.Replace("{", "").Replace("}", "").Replace("-", "_");

            FDatasmithFacadeActor currentActor = new FDatasmithFacadeActor(cleanName);

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
                BuildActorsHierarchyRecursive(child, currentActor, scene, actorsMap, ref actorsCreated);
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

        private static float[] ConvertMatrixToUnreal(float[] m)
        {
            var u = new float[16];
            // Row 0
            u[0] = m[0];
            u[1] = -m[4];
            u[2] = m[8];
            u[3] = m[12] * 100.0f;

            // Row 1
            u[4] = -m[1];
            u[5] = m[5];
            u[6] = -m[9];
            u[7] = -m[13] * 100.0f;

            // Row 2
            u[8] = m[2];
            u[9] = -m[6];
            u[10] = m[10];
            u[11] = m[14] * 100.0f;

            // Row 3
            u[12] = 0.0f;
            u[13] = 0.0f;
            u[14] = 0.0f;
            u[15] = 1.0f;

            return u;
        }

        private static string GetGeometryHash(List<float> vertices, List<int> indices)
        {
            using (var sha1 = System.Security.Cryptography.SHA1.Create())
            {
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var writer = new System.IO.BinaryWriter(ms))
                    {
                        foreach (float v in vertices) writer.Write(v);
                        foreach (int idx in indices) writer.Write(idx);
                        writer.Flush();
                        byte[] hashBytes = sha1.ComputeHash(ms.ToArray());
                        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                    }
                }
            }
        }
    }
}
