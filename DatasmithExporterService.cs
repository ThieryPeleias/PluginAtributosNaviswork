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
                    COMApi.InwOpSelection oSel = ComBridge.ToInwOpSelection(roots);
                    int processedPaths = 0;
                    int meshesExported = 0;

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

                                dynamic fragsColl = path.Fragments();
                                int fragsCount = fragsColl.Count;

                                for (int f = 1; f <= fragsCount; f++)
                                {
                                    try
                                    {
                                        dynamic frag = fragsColl.Item(f);
                                        if (frag == null) continue;

                                        var callback = new DatasmithGeometryCallback(frag.GetLocalToWorldMatrix());
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
                                        Log($"[Fragment Error] Failed to process fragment {f} on path: {fragEx.Message}");
                                    }
                                }

                                // Generate Datasmith mesh if geometry vertices are found
                                if (allVertices.Count > 0)
                                {
                                    using (var mesh = new FDatasmithFacadeMesh())
                                    {
                                        mesh.SetName(actorMesh.GetName() + "_Mesh");
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

                                        var meshElement = new FDatasmithFacadeMeshElement(actorMesh.GetName() + "_MeshElement");
                                        meshElement.SetLabel(actorMesh.GetLabel() + "_Mesh");

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

            FDatasmithFacadeActor currentActor = null;

            if (item.HasGeometry)
            {
                currentActor = new FDatasmithFacadeActorMesh(cleanName);
            }
            else
            {
                currentActor = new FDatasmithFacadeActor(cleanName);
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
    }
}
