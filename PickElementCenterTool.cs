using System;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;

namespace Virtuart4DNavisworks
{
    /// <summary>
    /// Custom Navisworks interactive ToolPlugin to pick an element from the viewport and get its bounding box center.
    /// </summary>
    [Plugin("PickElementCenterTool", "Virtuart4D", DisplayName = "Pick Element Center Tool", ToolTip = "Pick an element to select its center")]
    public class PickElementCenterTool : ToolPlugin
    {
        public static event Action<Point3D, ModelItem> ElementPicked;
        public static event Action SelectionCancelled;
        public static PickElementCenterTool Instance { get; private set; }

        public PickElementCenterTool()
        {
            Instance = this;
        }

        public static void ClearActiveOverlay()
        {
            if (Instance != null)
            {
                Instance._hasTarget = false;
                try
                {
                    Autodesk.Navisworks.Api.Application.ActiveDocument?.ActiveView?.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
                }
                catch { }
            }
        }

        private Point3D _currentPoint;
        private string _currentItemName = "";
        private string _currentItemType = "";
        private bool _hasTarget = false;

        public override Cursor GetCursor(View view, KeyModifiers modifier)
        {
            return Cursor.Application;
        }

        public override bool MouseMove(View view, KeyModifiers modifiers, int x, int y, double timeOffset)
        {
            try
            {
                PickItemResult result = view.PickItemFromPoint(x, y);
                if (result != null && result.ModelItem != null)
                {
                    ModelItem clickedItem = result.ModelItem;
                    _currentPoint = result.Point;
                    
                    // Extract name and parent/class type dynamically
                    _currentItemName = clickedItem.DisplayName ?? clickedItem.ClassDisplayName ?? "Element";
                    _currentItemType = clickedItem.ClassDisplayName ?? "";
                    if (clickedItem.Parent != null)
                    {
                        _currentItemType = clickedItem.Parent.DisplayName ?? clickedItem.Parent.ClassDisplayName ?? _currentItemType;
                    }

                    _hasTarget = true;
                    
                    // Force redraw to update overlays instantly on hover
                    view.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
                }
                else
                {
                    _hasTarget = false;
                    view.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Virtuart4D] PickElementCenterTool MouseMove error: {ex.Message}");
                _hasTarget = false;
            }

            return base.MouseMove(view, modifiers, x, y, timeOffset);
        }

        public override void OverlayRender(View view, Graphics graphics)
        {
            if (_hasTarget)
            {
                try
                {
                    // Project the 3D picked point to 2D screen coordinate
                    ProjectionResult proj = view.ProjectPoint(_currentPoint, true, true);
                    if (proj != null)
                    {
                        int sx = (int)proj.X;
                        int sy = (int)proj.Y;

                        // Begin window context for screen-space 2D overlay drawing
                        graphics.BeginWindowContext();

                        // 1. Draw a stylish indicator at the mouse position
                        // Small crosshair in theme color (#007586 -> 0, 117, 134)
                        graphics.LineWidth(2);
                        graphics.Color(Autodesk.Navisworks.Api.Color.FromByteRGB(0, 117, 134), 1.0);
                        graphics.Line(new Point3D(sx - 10, sy, 0), new Point3D(sx + 10, sy, 0));
                        graphics.Line(new Point3D(sx, sy - 10, 0), new Point3D(sx, sy + 10, 0));

                        // 2. Draw a clean, stylish tooltip card showing element attributes
                        string textLine1 = $"Item Name: {_currentItemName}";
                        string textLine2 = $"Item Type: {_currentItemType}";
                        string textLine3 = "Click to pick center";

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

                        // Card border (Datasmith theme color #007586)
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
                    if (result != null && result.ModelItem != null)
                    {
                        ModelItem clickedItem = result.ModelItem;
                        BoundingBox3D box = clickedItem.BoundingBox();
                        Point3D centerPoint = (box != null) ? box.Center : result.Point;

                        ElementPicked?.Invoke(centerPoint, clickedItem);

                        // Restore to standard selection tool and clean up
                        _hasTarget = false;
                        view.RequestDelayedRedraw(ViewRedrawRequests.OverlayRender);

                        Autodesk.Navisworks.Api.Application.ActiveDocument.Tool.Value = Tool.Select;
                        return true; // Click event handled
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Virtuart4D] PickElementCenterTool MouseDown error: {ex.Message}");
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
    }
}
