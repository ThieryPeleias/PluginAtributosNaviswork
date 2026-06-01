using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Navisworks.Api;
using ComBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;
using COMApi = Autodesk.Navisworks.Api.Interop.ComApi;

namespace Virtuart4DNavisworks
{
    /// <summary>
    /// Service that coordinates the export of Navisworks scenes using the Datasmith C# Facade SDK.
    /// </summary>
    public static class DatasmithExporterService
    {
        /// <summary>
        /// Exports the active Navisworks document to a Datasmith file.
        /// </summary>
        /// <param name="doc">The active Navisworks Document.</param>
        /// <param name="filePath">Target .udatasmith file path.</param>
        /// <returns>True if export was successful, false otherwise.</returns>
        public static bool ExportActiveDocument(Document doc, string filePath)
        {
            if (doc == null || string.IsNullOrEmpty(filePath)) return false;

            string outputDir = Path.GetDirectoryName(filePath);
            string sceneName = Path.GetFileNameWithoutExtension(filePath);

            if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            {
                throw new DirectoryNotFoundException($"The target directory does not exist: {outputDir}");
            }

            // Determine what to export: active selection if not empty, otherwise the entire model
            var selection = doc.CurrentSelection.SelectedItems;
            var roots = new ModelItemCollection();

            if (selection != null && selection.Count > 0)
            {
                // Export selection roots only
                roots.AddRange(selection);
            }
            else
            {
                // Export entire visible models
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
                return false;
            }

            // Initialize the Datasmith Scene Facade
            using (var scene = new FDatasmithFacadeScene("Navisworks", "Autodesk", "Navisworks Manage", "2025"))
            {
                scene.SetName(sceneName);
                scene.SetOutputPath(outputDir);
                scene.PreExport();

                // Recursively export all root items
                foreach (ModelItem rootItem in roots)
                {
                    ExportItemRecursive(rootItem, null, scene);
                }

                // Execute the final export
                bool success = scene.ExportScene(filePath);
                
                scene.CleanUp();

                return success;
            }
        }

        /// <summary>
        /// Recursively exports a ModelItem and its children to the Datasmith Scene.
        /// </summary>
        private static void ExportItemRecursive(ModelItem item, FDatasmithFacadeActor parentActor, FDatasmithFacadeScene scene)
        {
            // Skip hidden items to respect visibility rules in Navisworks
            if (item.IsHidden) return;

            // Generate a unique actor name using InstanceGuid or GetHashCode
            string guidStr = item.InstanceGuid != Guid.Empty ? item.InstanceGuid.ToString() : item.GetHashCode().ToString();
            string cleanName = "Node_" + guidStr.Replace("{", "").Replace("}", "").Replace("-", "_");

            FDatasmithFacadeActor currentActor = null;

            if (item.HasGeometry)
            {
                var actorMesh = new FDatasmithFacadeActorMesh(cleanName);
                actorMesh.SetLabel(item.DisplayName ?? item.ClassDisplayName ?? "Element");

                // Prepare mesh accumulator
                using (var mesh = new FDatasmithFacadeMesh())
                {
                    mesh.SetName(cleanName + "_Mesh");

                    var allVertices = new List<float>();
                    var allNormals = new List<float>();
                    var allIndices = new List<int>();

                    // Leverage COM API to extract low-level geometry fragments
                    var itemCollection = new ModelItemCollection { item };
                    COMApi.InwOpSelection oSel = ComBridge.ToInwOpSelection(itemCollection);

                    foreach (COMApi.InwOaPath3 path in oSel.Paths())
                    {
                        foreach (COMApi.InwOaFragment3 frag in path.Fragments())
                        {
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
                    }

                    // Populate Datasmith Mesh with geometry if any was found
                    if (allVertices.Count > 0)
                    {
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

                        // Export geometry to a .dasmesh file
                        FDatasmithFacadeMeshElement meshElement = scene.ExportDatasmithMesh(mesh);
                        if (meshElement != null)
                        {
                            scene.AddMesh(meshElement);
                            actorMesh.SetMesh(meshElement.GetName());
                        }
                    }
                }

                currentActor = actorMesh;
            }
            else
            {
                // Create standard actor for grouping nodes in the tree
                currentActor = new FDatasmithFacadeActor(cleanName);
                currentActor.SetLabel(item.DisplayName ?? item.ClassDisplayName ?? "Group");
            }

            if (currentActor != null)
            {
                // Extract properties/attributes and map to Datasmith Metadata
                ExportProperties(item, currentActor, scene);

                // Add to hierarchy
                if (parentActor != null)
                {
                    parentActor.AddChild(currentActor);
                }
                else
                {
                    scene.AddActor(currentActor);
                }

                // Recursively export all children nodes
                foreach (ModelItem child in item.Children)
                {
                    ExportItemRecursive(child, currentActor, scene);
                }
            }
        }

        /// <summary>
        /// Maps all native and custom properties of a ModelItem to Datasmith Metadata.
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
