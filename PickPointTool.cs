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
        public static event Action SelectionCancelled;
        public static PickPointTool Instance { get; private set; }

        public PickPointTool()
        {
            Instance = this;
        }

        public static void ClearActiveOverlay()
        {
            if (Instance != null)
            {
                Instance._hasSnap = false;
                try
                {
                    Autodesk.Navisworks.Api.Application.ActiveDocument?.ActiveView?.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
                }
                catch { }
            }
        }

        private Point3D _currentSnappedPoint;
        private string _currentItemName = "";
        private string _currentItemType = "";
        private bool _hasSnap = false;

        public override Cursor GetCursor(View view, KeyModifiers modifier)
        {
            return Cursor.MeasureVertex;
        }

        public override bool MouseMove(View view, KeyModifiers modifiers, int x, int y, double timeOffset)
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
                        _currentSnappedPoint = SnapToNearestVertex(clickedItem, pickedPoint);
                        
                        // Extract name and parent/class type dynamically
                        _currentItemName = clickedItem.DisplayName ?? clickedItem.ClassDisplayName ?? "Element";
                        _currentItemType = clickedItem.ClassDisplayName ?? "";
                        if (clickedItem.Parent != null)
                        {
                            _currentItemType = clickedItem.Parent.DisplayName ?? clickedItem.Parent.ClassDisplayName ?? _currentItemType;
                        }

                        _hasSnap = true;
                    }
                    else
                    {
                        _hasSnap = false;
                    }

                    // Force redraw to update overlays instantly on hover
                    view.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
                }
                else
                {
                    _hasSnap = false;
                    view.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Virtuart4D] MouseMove error: {ex.Message}");
                _hasSnap = false;
            }

            return base.MouseMove(view, modifiers, x, y, timeOffset);
        }

        public override void OverlayRender(View view, Graphics graphics)
        {
            if (_hasSnap)
            {
                try
                {
                    // Project the 3D snapped vertex to 2D screen coordinate
                    ProjectionResult proj = view.ProjectPoint(_currentSnappedPoint, true, true);
                    if (proj != null)
                    {
                        int sx = (int)proj.X;
                        int sy = (int)proj.Y;

                        // Begin window context for screen-space 2D overlay drawing
                        graphics.BeginWindowContext();

                        // 1. Draw a high-precision snap indicator (Crosshair and circle/square)
                        // Red horizontal line
                        graphics.LineWidth(2);
                        graphics.Color(Autodesk.Navisworks.Api.Color.FromByteRGB(255, 0, 0), 1.0);
                        graphics.Line(new Point3D(sx - 12, sy, 0), new Point3D(sx + 12, sy, 0));
                        
                        // Green vertical line
                        graphics.Color(Autodesk.Navisworks.Api.Color.FromByteRGB(0, 255, 0), 1.0);
                        graphics.Line(new Point3D(sx, sy - 12, 0), new Point3D(sx, sy + 12, 0));

                        // Blue bounding square around the snapped vertex
                        graphics.Color(Autodesk.Navisworks.Api.Color.FromByteRGB(0, 0, 255), 1.0);
                        graphics.Line(new Point3D(sx - 5, sy - 5, 0), new Point3D(sx + 5, sy - 5, 0));
                        graphics.Line(new Point3D(sx + 5, sy - 5, 0), new Point3D(sx + 5, sy + 5, 0));
                        graphics.Line(new Point3D(sx + 5, sy + 5, 0), new Point3D(sx - 5, sy + 5, 0));
                        graphics.Line(new Point3D(sx - 5, sy + 5, 0), new Point3D(sx - 5, sy - 5, 0));

                        // 2. Draw a clean, stylish tooltip card showing element attributes
                        string textLine1 = $"Item Name: {_currentItemName}";
                        string textLine2 = $"Item Type: {_currentItemType}";
                        string textLine3 = "Click to snap vertex";

                        // Use a professional typeface
                        TextFontInfo font = new TextFontInfo("Segoe UI", 9, 4, false, false);
                        
                        // Calculate width based on text length (approx 7px per character)
                        int cardW = Math.Max(Math.Max(textLine1.Length, textLine2.Length), textLine3.Length) * 7 + 16;
                        cardW = Math.Max(cardW, 160);
                        int cardH = 56;

                        // Offset the tooltip card slightly bottom-right from the cursor
                        int cardX = sx + 15;
                        int cardY = sy + 15;

                        // Background card (semi-transparent white)
                        graphics.Color(Autodesk.Navisworks.Api.Color.FromByteRGB(255, 255, 255), 0.9);
                        graphics.Rectangle(new Point2D(cardX, cardY), new Point2D(cardX + cardW, cardY + cardH), true);

                        // Card border (Datasmith sleek cyan/teal)
                        graphics.LineWidth(1);
                        graphics.Color(Autodesk.Navisworks.Api.Color.FromByteRGB(0, 117, 134), 1.0);
                        graphics.Line(new Point3D(cardX, cardY, 0), new Point3D(cardX + cardW, cardY, 0));
                        graphics.Line(new Point3D(cardX + cardW, cardY, 0), new Point3D(cardX + cardW, cardY + cardH, 0));
                        graphics.Line(new Point3D(cardX + cardW, cardY + cardH, 0), new Point3D(cardX, cardY + cardH, 0));
                        graphics.Line(new Point3D(cardX, cardY + cardH, 0), new Point3D(cardX, cardY, 0));

                        // Render text inside the card
                        graphics.Color(Autodesk.Navisworks.Api.Color.FromByteRGB(30, 30, 30), 1.0);
                        graphics.Text2D(font, textLine1, new Point2D(cardX + 8, cardY + 6), 0, 0);
                        graphics.Text2D(font, textLine2, new Point2D(cardX + 8, cardY + 22), 0, 0);

                        graphics.Color(Autodesk.Navisworks.Api.Color.FromByteRGB(0, 117, 134), 1.0);
                        graphics.Text2D(font, textLine3, new Point2D(cardX + 8, cardY + 38), 0, 0);

                        // End window context
                        graphics.EndWindowContext();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Virtuart4D] OverlayRender error: {ex.Message}");
                }
            }
        }

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

                        // Restore to standard selection tool and clean up
                        _hasSnap = false;
                        view.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);

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

        public override bool KeyDown(View view, KeyModifiers modifiers, ushort key, double timeOffset)
        {
            if (key == 27) // Esc key
            {
                ClearActiveOverlay();

                Autodesk.Navisworks.Api.Application.ActiveDocument.Tool.Value = Tool.Select;

                SelectionCancelled?.Invoke();
                return true; // Handled
            }
            return base.KeyDown(view, modifiers, key, timeOffset);
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
