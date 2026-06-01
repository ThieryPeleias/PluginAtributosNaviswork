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
                    int fragmentActorsCreated = 0;

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
                            string parentName = parentActor.GetName();
                            string parentLabel = parentActor.GetLabel();

                            for (int f = 1; f <= fragsCount; f++)
                            {
                                try
                                {
                                    dynamic frag = fragsColl.Item(f);
                                    if (frag == null) continue;

                                    // Extract geometry in LOCAL space (null matrix = no world bake)
                                    var callback = new DatasmithGeometryCallback(null);
                                    frag.GenerateSimplePrimitives(COMApi.nwEVertexProperty.eNORMAL, callback);

                                    if (callback.Vertices.Count == 0) continue;

                                    // Decompose fragment's local-to-world matrix into transform components
                                    DatasmithGeometryCallback.DecomposeMatrix(
                                        frag.GetLocalToWorldMatrix(),
                                        out float tx, out float ty, out float tz,
                                        out float qx, out float qy, out float qz, out float qw,
                                        out float sx, out float sy, out float sz);

                                    // Create mesh for this fragment
                                    string meshName = $"{parentName}_Mesh_{f}";
                                    string meshElementName = $"{parentName}_Mesh_{f}Element";
                                    string actorName = $"{parentName}_Actor_{f}";
                                    string actorLabel = $"{parentLabel}_{f}";

                                    using (var mesh = new FDatasmithFacadeMesh())
                                    {
                                        mesh.SetName(meshName);
                                        int vertCount = callback.Vertices.Count / 3;
                                        mesh.SetVerticesCount(vertCount);
                                        for (int i = 0; i < vertCount; i++)
                                        {
                                            // Vertices are in local fragment space; apply scale+Y-invert for Unreal coord system
                                            float vx = callback.Vertices[i * 3] * 100.0f;
                                            float vy = -callback.Vertices[i * 3 + 1] * 100.0f;
                                            float vz = callback.Vertices[i * 3 + 2] * 100.0f;
                                            mesh.SetVertex(i, vx, vy, vz);

                                            float nx = callback.Normals[i * 3];
                                            float ny = -callback.Normals[i * 3 + 1];
                                            float nz = callback.Normals[i * 3 + 2];
                                            mesh.SetNormal(i, nx, ny, nz);
                                        }

                                        int faceCount = callback.Indices.Count / 3;
                                        mesh.SetFacesCount(faceCount);
                                        for (int i = 0; i < faceCount; i++)
                                        {
                                            mesh.SetFace(i, callback.Indices[i * 3], callback.Indices[i * 3 + 1], callback.Indices[i * 3 + 2]);
                                        }

                                        var meshElement = new FDatasmithFacadeMeshElement(meshElementName);
                                        meshElement.SetLabel(actorLabel + "_Mesh");

                                        bool meshSuccess = scene.ExportDatasmithMesh(meshElement, mesh);
                                        if (meshSuccess)
                                        {
                                            scene.AddMesh(meshElement);

                                            // Create fragment ActorMesh child with decomposed transform
                                            var fragActorMesh = new FDatasmithFacadeActorMesh(actorName);
                                            fragActorMesh.SetLabel(actorLabel);
                                            fragActorMesh.SetMesh(meshElementName);

                                            // Set the fragment's world transform (decomposed from its local-to-world matrix)
                                            fragActorMesh.SetTranslation(tx, ty, tz);
                                            fragActorMesh.SetRotation(qx, qy, qz, qw);
                                            fragActorMesh.SetScale(sx, sy, sz);

                                            parentActor.AddChild(fragActorMesh);
                                            fragmentActorsCreated++;
                                            meshesExported++;
                                        }
                                        else
                                        {
                                            Log($"[Mesh Error] ExportDatasmithMesh returned false for: {meshElementName}");
                                            meshElement.Dispose();
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
                            Log($"Progress: Processed {processedPaths} leaf nodes. Created {fragmentActorsCreated} fragment actors, exported {meshesExported} meshes...");
                        }
                    }
                    Log($"Step 2 Completed. Processed {processedPaths} paths. Created {fragmentActorsCreated} fragment actors. Exported {meshesExported} meshes.");

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

            // All hierarchy nodes are grouping actors. Leaf items with geometry
            // will get per-fragment ActorMesh children added during Step 2.
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
    }
}
