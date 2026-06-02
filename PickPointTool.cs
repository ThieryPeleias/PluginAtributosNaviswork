using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using COMApi = Autodesk.Navisworks.Api.Interop.ComApi;
using ComBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;

namespace Virtuart4DNavisworks
{
    /// <summary>
    /// Custom Navisworks interactive ToolPlugin to pick a point from the viewport and snap to the nearest vertex.
    /// </summary>
    [Plugin("PickPointTool", "Virtuart4D", DisplayName = "Pick Point Tool", ToolTip = "Pick a point on a 3D vertex")]
    public class PickPointTool : ToolPlugin
    {
        public static event Action<Point3D> PointPicked;

        public override bool MouseDown(View view, KeyModifiers modifiers, ushort button, int x, int y, double timeOffset)
        {
            if (button == 1) // Left click
            {
                try
                {
                    PickItemResult result = view.PickItemFromPoint(x, y);
                    if (result != null)
                    {
                        Point3D pickedPoint = result.Point;
                        ModelItem clickedItem = result.ModelItem;

                        if (clickedItem != null)
                        {
                            Point3D snappedPoint = SnapToNearestVertex(clickedItem, pickedPoint);
                            PointPicked?.Invoke(snappedPoint);
                        }
                        else
                        {
                            PointPicked?.Invoke(pickedPoint);
                        }

                        // Restore to standard selection tool
                        Autodesk.Navisworks.Api.Application.ActiveDocument.Tool.Value = Tool.Select;
                        return true; // Click event handled
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Virtuart4D] PickPointTool MouseDown error: {ex.Message}");
                }
            }

            return base.MouseDown(view, modifiers, button, x, y, timeOffset);
        }

        private Point3D SnapToNearestVertex(ModelItem item, Point3D targetPoint)
        {
            try
            {
                var collector = new VertexCollectorCallback();
                COMApi.InwOpState10 oState = (COMApi.InwOpState10)ComBridge.State;
                
                var itemColl = new ModelItemCollection { item };
                var sel = ComBridge.ToInwOpSelection(itemColl);
                
                foreach (COMApi.InwOaPath path in sel.Paths())
                {
                    foreach (COMApi.InwOaFragment3 frag in path.Fragments())
                    {
                        COMApi.InwLTransform3f localToWorld = frag.GetLocalToWorldMatrix();
                        float[] matrix = DatasmithGeometryCallback.ExtractMatrix(localToWorld);
                        
                        collector.SetTransform(matrix);
                        frag.GenerateSimplePrimitives(COMApi.nwEVertexProperty.eNONE, collector);
                    }
                }

                if (collector.Vertices.Count > 0)
                {
                    Point3D nearest = collector.Vertices[0];
                    double minDistSq = DistanceSquared(nearest, targetPoint);
                    for (int i = 1; i < collector.Vertices.Count; i++)
                    {
                        Point3D pt = collector.Vertices[i];
                        double distSq = DistanceSquared(pt, targetPoint);
                        if (distSq < minDistSq)
                        {
                            minDistSq = distSq;
                            nearest = pt;
                        }
                    }
                    return nearest;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Virtuart4D] Snapping error, falling back to raw pick: {ex.Message}");
            }
            return targetPoint;
        }

        private double DistanceSquared(Point3D p1, Point3D p2)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            double dz = p1.Z - p2.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }

    /// <summary>
    /// Custom callback to collect vertex coordinates from geometric primitives.
    /// </summary>
    public class VertexCollectorCallback : COMApi.InwSimplePrimitivesCB
    {
        private float[] _matrix;
        public List<Point3D> Vertices { get; } = new List<Point3D>();

        public void SetTransform(float[] matrix)
        {
            _matrix = matrix;
        }

        public void Triangle(COMApi.InwSimpleVertex v1, COMApi.InwSimpleVertex v2, COMApi.InwSimpleVertex v3)
        {
            AddVertex(v1);
            AddVertex(v2);
            AddVertex(v3);
        }

        public void Line(COMApi.InwSimpleVertex v1, COMApi.InwSimpleVertex v2)
        {
            AddVertex(v1);
            AddVertex(v2);
        }

        public void Point(COMApi.InwSimpleVertex v1)
        {
            AddVertex(v1);
        }

        public void SnapPoint(COMApi.InwSimpleVertex v1)
        {
            AddVertex(v1);
        }

        private void AddVertex(COMApi.InwSimpleVertex v)
        {
            Array arr = v.coord as Array;
            if (arr != null && _matrix != null)
            {
                double[] localCoords = new double[3];
                arr.CopyTo(localCoords, 0);
                float lx = (float)localCoords[0];
                float ly = (float)localCoords[1];
                float lz = (float)localCoords[2];

                // Apply Local-to-World transform matrix
                float rx = lx * _matrix[0] + ly * _matrix[4] + lz * _matrix[8] + _matrix[12];
                float ry = lx * _matrix[1] + ly * _matrix[5] + lz * _matrix[9] + _matrix[13];
                float rz = lx * _matrix[2] + ly * _matrix[6] + lz * _matrix[10] + _matrix[14];

                Vertices.Add(new Point3D(rx, ry, rz));
            }
        }
    }
}
