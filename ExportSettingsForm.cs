using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace Virtuart4DNavisworks
{
    public class ExportSettingsForm : Form
    {
        // Statically cached settings to persist between runs during the session
        private static int _cachedMergeDepth = 0;
        private static double _cachedOriginX = 0;
        private static double _cachedOriginY = 0;
        private static double _cachedOriginZ = 0;
        private static System.Collections.Generic.List<string[]> _cachedGroupByProperties = new System.Collections.Generic.List<string[]>();

        // Discovered properties of the model
        private static System.Collections.Generic.List<string> _discoveredCategories = new System.Collections.Generic.List<string>();
        private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> _discoveredProperties = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>(StringComparer.OrdinalIgnoreCase);

        // Origin selection tracking
        private static string _originSelectionType = "Default / Manual";
        private static string _selectedElementName = "None";
        private static string _selectedElementId = "None";
        private static double _pickedX = 0;
        private static double _pickedY = 0;
        private static double _pickedZ = 0;

        private NumericUpDown numMergeDepth;
        private TextBox txtOriginX;
        private TextBox txtOriginY;
        private TextBox txtOriginZ;
        private Button btnPickVertex;
        private Button btnPickSelected;
        private Button btnResetOrigin;
        private Button btnExport;
        private Button btnCancel;
        private Label lblSelectedInfo;

        private FlowLayoutPanel flpProperties;
        private Button btnAddProperty;

        public ExportSettingsForm()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DiscoverModelProperties();
            InitializeUI();
            RegisterMouseMoveRecursive(this);
        }

        private void InitializeUI()
        {
            Text = "Virtuart4D - Export Settings";
            Size = new Size(520, 720);
            MinimumSize = new Size(520, 720);
            MaximumSize = new Size(520, 720);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = UITheme.Color.Background;
            Font = UITheme.Typography.Body;

            // ── Header ─────────────────────────────────────────────────
            var pnlHeader = UITheme.CreateHeader("Datasmith Export Settings",
                "Configure hierarchy merging and spatial coordinate origin");
            Controls.Add(pnlHeader);

            // ── Footer ─────────────────────────────────────────────────
            var pnlFooter = UITheme.CreateFooter();
            
            btnCancel = UITheme.CreateSecondaryButton("Cancel");
            btnCancel.Width = 90;
            btnCancel.Location = new Point(310, 8);
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            pnlFooter.Controls.Add(btnCancel);

            btnExport = UITheme.CreatePrimaryButton("Export");
            btnExport.Width = 90;
            btnExport.Location = new Point(410, 8);
            btnExport.Click += BtnExport_Click;
            pnlFooter.Controls.Add(btnExport);

            Controls.Add(pnlFooter);
            AcceptButton = btnExport;
            CancelButton = btnCancel;

            // ── Body / Content Card ────────────────────────────────────
            var pnlBody = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(UITheme.Spacing.LG)
            };
            Controls.Add(pnlBody);
            pnlBody.BringToFront();

            var card = UITheme.CreateCard();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(UITheme.Spacing.LG);
            pnlBody.Controls.Add(card);

            int y = UITheme.Spacing.MD;

            // ── Section 1: Hierarchy Merging ───────────────────────────
            var lblMergeTitle = new Label
            {
                Text = "HIERARCHY MERGING (MERGE DEPTH)",
                Font = UITheme.Typography.Label,
                ForeColor = UITheme.Color.Primary,
                Location = new Point(UITheme.Spacing.LG, y),
                AutoSize = true
            };
            card.Controls.Add(lblMergeTitle);
            y += 20;

            var lblMergeHint = new Label
            {
                Text = "0 = No Merging (preserves every component as separate mesh).\n3+ = High Merging (groups deep subtrees, fast and highly optimized).",
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextSecondary,
                Location = new Point(UITheme.Spacing.LG, y),
                Size = new Size(420, 32)
            };
            card.Controls.Add(lblMergeHint);
            y += 34;

            numMergeDepth = new NumericUpDown
            {
                Font = UITheme.Typography.Body,
                Location = new Point(UITheme.Spacing.LG, y),
                Width = 100,
                Minimum = 0,
                Maximum = 10,
                Value = _cachedMergeDepth
            };
            card.Controls.Add(numMergeDepth);
            y += 40;

            // ── Divider ────────────────────────────────────────────────
            var div = UITheme.CreateDivider();
            div.Location = new Point(UITheme.Spacing.LG, y);
            div.Width = 430;
            card.Controls.Add(div);
            y += 16;

            // ── Section 2: Spatial Coordinate Origin ───────────────────
            var lblOriginTitle = new Label
            {
                Text = "SPATIAL COORDINATE ORIGIN (X, Y, Z)",
                Font = UITheme.Typography.Label,
                ForeColor = UITheme.Color.Primary,
                Location = new Point(UITheme.Spacing.LG, y),
                AutoSize = true
            };
            card.Controls.Add(lblOriginTitle);
            y += 20;

            var lblOriginHint = new Label
            {
                Text = "Subtracts this reference point in Navisworks meters to ensure precision.\nBecomes (0, 0, 0) inside Unreal Engine.",
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextSecondary,
                Location = new Point(UITheme.Spacing.LG, y),
                Size = new Size(420, 32)
            };
            card.Controls.Add(lblOriginHint);
            y += 34;

            // Coord Inputs
            int lx = UITheme.Spacing.LG;
            
            var lblX = new Label { Text = "X (m):", Font = UITheme.Typography.BodySmall, ForeColor = UITheme.Color.TextPrimary, Location = new Point(lx, y), AutoSize = true };
            card.Controls.Add(lblX);
            txtOriginX = new TextBox { Location = new Point(lx, y + 16), Width = 100, Text = _cachedOriginX.ToString("F3") };
            UITheme.StyleTextBox(txtOriginX);
            card.Controls.Add(txtOriginX);
            lx += 115;

            var lblY = new Label { Text = "Y (m):", Font = UITheme.Typography.BodySmall, ForeColor = UITheme.Color.TextPrimary, Location = new Point(lx, y), AutoSize = true };
            card.Controls.Add(lblY);
            txtOriginY = new TextBox { Location = new Point(lx, y + 16), Width = 100, Text = _cachedOriginY.ToString("F3") };
            UITheme.StyleTextBox(txtOriginY);
            card.Controls.Add(txtOriginY);
            lx += 115;

            var lblZ = new Label { Text = "Z (m):", Font = UITheme.Typography.BodySmall, ForeColor = UITheme.Color.TextPrimary, Location = new Point(lx, y), AutoSize = true };
            card.Controls.Add(lblZ);
            txtOriginZ = new TextBox { Location = new Point(lx, y + 16), Width = 100, Text = _cachedOriginZ.ToString("F3") };
            UITheme.StyleTextBox(txtOriginZ);
            card.Controls.Add(txtOriginZ);
            
            y += 56;

            // Pick / Reset Buttons
            btnPickVertex = UITheme.CreatePrimaryButton("Pick Vertex with Snap");
            btnPickVertex.Width = 170;
            btnPickVertex.Location = new Point(UITheme.Spacing.LG, y);
            btnPickVertex.Click += BtnPickVertex_Click;
            card.Controls.Add(btnPickVertex);

            btnPickSelected = UITheme.CreatePrimaryButton("Pick Selection Center");
            btnPickSelected.Width = 170;
            btnPickSelected.Location = new Point(UITheme.Spacing.LG + 178, y);
            btnPickSelected.Click += BtnPickSelected_Click;
            card.Controls.Add(btnPickSelected);

            btnResetOrigin = UITheme.CreateSecondaryButton("Reset to Zero");
            btnResetOrigin.Width = 100;
            btnResetOrigin.Location = new Point(UITheme.Spacing.LG + 356, y);
            btnResetOrigin.Click += BtnResetOrigin_Click;
            card.Controls.Add(btnResetOrigin);

            y += 42;

            // Selected Item Info Label
            lblSelectedInfo = new Label
            {
                Text = "",
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.Success,
                Location = new Point(UITheme.Spacing.LG, y),
                Size = new Size(420, 20),
                AutoEllipsis = true
            };
            card.Controls.Add(lblSelectedInfo);
            
            // Check active selection and show immediate status
            UpdateSelectionInfo();

            // Subscribe to real-time selection changes in the Navisworks scene
            try
            {
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc != null && doc.CurrentSelection != null)
                {
                    doc.CurrentSelection.Changed += CurrentSelection_Changed;
                }
            }
            catch { }

            y += 26;

            // ── Divider ──
            var div2 = UITheme.CreateDivider();
            div2.Location = new Point(UITheme.Spacing.LG, y);
            div2.Width = 430;
            card.Controls.Add(div2);
            y += 16;

            // ── Section 3: Group by Property ──
            var lblGroupTitle = new Label
            {
                Text = "SMART MERGING / GROUPING BY PROPERTIES",
                Font = UITheme.Typography.Label,
                ForeColor = UITheme.Color.Primary,
                Location = new Point(UITheme.Spacing.LG, y),
                Width = 280,
                AutoSize = true
            };
            card.Controls.Add(lblGroupTitle);

            btnAddProperty = UITheme.CreatePrimaryButton("+ Add Attribute");
            btnAddProperty.Width = 110;
            btnAddProperty.Height = 24;
            btnAddProperty.Font = UITheme.Typography.BodySmall;
            btnAddProperty.Location = new Point(UITheme.Spacing.LG + 300, y - 4);
            btnAddProperty.Click += (s, e) => AddPropertyRow();
            card.Controls.Add(btnAddProperty);

            y += 22;

            var lblGroupHint = new Label
            {
                Text = "Restructures udatasmith file: groups geometries under intermediate parent folders\nrepresenting unique combinations of attribute values (e.g. Set A | Phase 1).",
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextSecondary,
                Location = new Point(UITheme.Spacing.LG, y),
                Size = new Size(420, 32)
            };
            card.Controls.Add(lblGroupHint);
            y += 34;

            flpProperties = new FlowLayoutPanel
            {
                Location = new Point(UITheme.Spacing.LG, y),
                Width = 430,
                Height = 105,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            card.Controls.Add(flpProperties);

            // Populate cached properties
            if (_cachedGroupByProperties != null && _cachedGroupByProperties.Count > 0)
            {
                foreach (var propPair in _cachedGroupByProperties)
                {
                    AddPropertyRow(propPair[0], propPair[1]);
                }
            }
        }

        private void UpdateSelectionInfo()
        {
            try
            {
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc != null && doc.CurrentSelection != null && doc.CurrentSelection.SelectedItems.Count > 0)
                {
                    int count = doc.CurrentSelection.SelectedItems.Count;
                    lblSelectedInfo.Text = $"✓ {count} active element(s) selected ready to pick.";
                    lblSelectedInfo.ForeColor = UITheme.Color.Success;
                }
                else
                {
                    lblSelectedInfo.Text = "ℹ Select an element in the tree/view to pick its center.";
                    lblSelectedInfo.ForeColor = UITheme.Color.TextTertiary;
                }
            }
            catch
            {
                lblSelectedInfo.Text = "";
            }
        }

        private void DiscoverModelProperties()
        {
            try
            {
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc == null || doc.Models.Count == 0) return;

                var sampleItems = new System.Collections.Generic.List<ModelItem>();
                
                // If there's selection, sample from selection first
                if (doc.CurrentSelection != null && doc.CurrentSelection.SelectedItems.Count > 0)
                {
                    foreach (var item in doc.CurrentSelection.SelectedItems)
                    {
                        CollectSampleItems(item, sampleItems, 300);
                        if (sampleItems.Count >= 300) break;
                    }
                }
                
                // Also sample from main models if we don't have enough selection items
                if (sampleItems.Count < 100)
                {
                    foreach (var model in doc.Models)
                    {
                        if (model.RootItem != null)
                        {
                            CollectSampleItems(model.RootItem, sampleItems, 300);
                            if (sampleItems.Count >= 300) break;
                        }
                    }
                }

                var tempCategories = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var tempProps = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in sampleItems)
                {
                    if (item == null) continue;
                    foreach (var category in item.PropertyCategories)
                    {
                        string catName = category.DisplayName ?? category.Name;
                        if (string.IsNullOrEmpty(catName)) continue;

                        tempCategories.Add(catName);

                        if (!tempProps.TryGetValue(catName, out var props))
                        {
                            props = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            tempProps[catName] = props;
                        }

                        foreach (var prop in category.Properties)
                        {
                            string propName = prop.DisplayName ?? prop.Name;
                            if (!string.IsNullOrEmpty(propName))
                            {
                                props.Add(propName);
                            }
                        }
                    }
                }

                _discoveredCategories = tempCategories.OrderBy(c => c).ToList();
                _discoveredProperties.Clear();
                foreach (var kvp in tempProps)
                {
                    _discoveredProperties[kvp.Key] = kvp.Value.OrderBy(p => p).ToList();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Virtuart4D] Error discovering properties: {ex.Message}");
            }
        }

        private void CollectSampleItems(ModelItem item, System.Collections.Generic.List<ModelItem> list, int limit)
        {
            if (item == null || list.Count >= limit) return;
            if (item.HasGeometry && !System.Linq.Enumerable.Any(item.Children))
            {
                list.Add(item);
            }
            foreach (var child in item.Children)
            {
                CollectSampleItems(child, list, limit);
                if (list.Count >= limit) return;
            }
        }

        private void AddPropertyRow(string initialCat = "", string initialProp = "")
        {
            if (flpProperties.Controls.Count >= 3)
            {
                return;
            }

            var rowPanel = new Panel
            {
                Width = flpProperties.Width - 25,
                Height = 30,
                Margin = new Padding(0, 0, 0, 0)
            };

            var cbCategory = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = UITheme.Typography.BodySmall,
                Location = new Point(0, 2),
                Width = 180
            };

            var cbProperty = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = UITheme.Typography.BodySmall,
                Location = new Point(190, 2),
                Width = 180
            };

            var btnDelete = new Button
            {
                Text = "×",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = System.Drawing.Color.White,
                BackColor = System.Drawing.Color.FromArgb(183, 28, 28), // UITheme.Color.Error
                FlatStyle = FlatStyle.Flat,
                Location = new Point(380, 1),
                Width = 26,
                Height = 25,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter
            };
            btnDelete.FlatAppearance.BorderSize = 0;

            // Populate Category
            cbCategory.Items.AddRange(_discoveredCategories.ToArray());
            if (!string.IsNullOrEmpty(initialCat))
            {
                cbCategory.Text = initialCat;
            }
            else if (_discoveredCategories.Count > 0)
            {
                cbCategory.Text = _discoveredCategories.Contains(VirtuartSchema.CategoriaPrincipal) ? VirtuartSchema.CategoriaPrincipal : _discoveredCategories[0];
            }

            // Update Properties based on Category
            Action updateProperties = () =>
            {
                string selectedCat = cbCategory.Text;
                var currentText = cbProperty.Text;
                cbProperty.Items.Clear();
                if (_discoveredProperties.TryGetValue(selectedCat, out var props))
                {
                    cbProperty.Items.AddRange(props.ToArray());
                    if (props.Contains(currentText))
                    {
                        cbProperty.Text = currentText;
                    }
                    else if (props.Count > 0)
                    {
                        cbProperty.Text = props[0];
                    }
                }
            };

            cbCategory.SelectedIndexChanged += (s, e) => updateProperties();
            cbCategory.TextChanged += (s, e) => updateProperties();

            updateProperties();
            if (!string.IsNullOrEmpty(initialProp))
            {
                cbProperty.Text = initialProp;
            }

            btnDelete.Click += (s, e) =>
            {
                flpProperties.Controls.Remove(rowPanel);
                rowPanel.Dispose();
                UpdateAddButtonState();
            };

            rowPanel.Controls.Add(cbCategory);
            rowPanel.Controls.Add(cbProperty);
            rowPanel.Controls.Add(btnDelete);

            flpProperties.Controls.Add(rowPanel);
            UpdateAddButtonState();
        }

        private void UpdateAddButtonState()
        {
            btnAddProperty.Enabled = flpProperties.Controls.Count < 3;
        }

        private void BtnPickSelected_Click(object sender, EventArgs e)
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null) return;

            if (doc.CurrentSelection == null || doc.CurrentSelection.SelectedItems.IsEmpty)
            {
                try
                {
                    var record = (Autodesk.Navisworks.Api.Plugins.ToolPluginRecord)
                        Autodesk.Navisworks.Api.Application.Plugins.FindPlugin("PickElementCenterTool.Virtuart4D");
                    if (record != null)
                    {
                        PickElementCenterTool.ElementPicked -= PickElementCenterTool_ElementPicked;
                        PickElementCenterTool.ElementPicked += PickElementCenterTool_ElementPicked;
                        PickElementCenterTool.SelectionCancelled -= PickTool_Cancelled;
                        PickElementCenterTool.SelectionCancelled += PickTool_Cancelled;

                        Autodesk.Navisworks.Api.Application.MainDocument.Tool.SetCustomToolPlugin(record.LoadPlugin());

                        lblSelectedInfo.Text = "⚡ Click a 3D element in the viewport to pick its center...";
                        lblSelectedInfo.ForeColor = UITheme.Color.Warning;
                    }
                    else
                    {
                        MessageBox.Show("Could not load the custom Pick Element Center tool plugin.",
                            "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to activate element center pick tool: {ex.Message}",
                        "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            try
            {
                BoundingBox3D totalBox = null;
                foreach (ModelItem item in doc.CurrentSelection.SelectedItems)
                {
                    BoundingBox3D box = item.BoundingBox();
                    if (box != null)
                    {
                        if (totalBox == null)
                        {
                            totalBox = box;
                        }
                        else
                        {
                            totalBox = totalBox.Extend(box);
                        }
                    }
                }

                if (totalBox != null)
                {
                    var center = totalBox.Center;
                    txtOriginX.Text = center.X.ToString("F3");
                    txtOriginY.Text = center.Y.ToString("F3");
                    txtOriginZ.Text = center.Z.ToString("F3");

                    _pickedX = center.X;
                    _pickedY = center.Y;
                    _pickedZ = center.Z;

                    string label = "Element";
                    string uniqueId = "None";
                    if (doc.CurrentSelection.SelectedItems.Count == 1)
                    {
                        var item = doc.CurrentSelection.SelectedItems[0];
                        label = item.DisplayName ?? item.ClassDisplayName ?? "Element";
                        uniqueId = GetModelItemUniqueId(item);
                    }
                    else
                    {
                        label = $"{doc.CurrentSelection.SelectedItems.Count} elements";
                        var ids = new System.Collections.Generic.List<string>();
                        foreach (ModelItem item in doc.CurrentSelection.SelectedItems)
                        {
                            ids.Add(GetModelItemUniqueId(item));
                        }
                        uniqueId = string.Join(", ", ids);
                    }

                    _originSelectionType = "Element Center";
                    _selectedElementName = label;
                    _selectedElementId = uniqueId;

                    lblSelectedInfo.Text = $"✓ Picked origin from center of: \"{label ?? "Element"}\"";
                    lblSelectedInfo.ForeColor = UITheme.Color.Success;
                }
                else
                {
                    MessageBox.Show("Could not calculate bounding box center of active selection.",
                        "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to retrieve coordinates: {ex.Message}",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnResetOrigin_Click(object sender, EventArgs e)
        {
            txtOriginX.Text = "0.000";
            txtOriginY.Text = "0.000";
            txtOriginZ.Text = "0.000";
            _originSelectionType = "Default / Manual";
            _selectedElementName = "None";
            _selectedElementId = "None";
            _pickedX = 0;
            _pickedY = 0;
            _pickedZ = 0;
            lblSelectedInfo.Text = "Origin coordinates reset to (0,0,0).";
            lblSelectedInfo.ForeColor = UITheme.Color.TextSecondary;
        }

        private void BtnPickVertex_Click(object sender, EventArgs e)
        {
            try
            {
                var record = (Autodesk.Navisworks.Api.Plugins.ToolPluginRecord)
                    Autodesk.Navisworks.Api.Application.Plugins.FindPlugin("PickPointTool.Virtuart4D");
                if (record != null)
                {
                    PickPointTool.PointPicked -= PickPointTool_PointPicked;
                    PickPointTool.PointPicked += PickPointTool_PointPicked;
                    PickPointTool.SelectionCancelled -= PickTool_Cancelled;
                    PickPointTool.SelectionCancelled += PickTool_Cancelled;

                    Autodesk.Navisworks.Api.Application.MainDocument.Tool.SetCustomToolPlugin(record.LoadPlugin());

                    lblSelectedInfo.Text = "⚡ Click a 3D vertex in the viewport...";
                    lblSelectedInfo.ForeColor = UITheme.Color.Warning;
                }
                else
                {
                    MessageBox.Show("Could not load the custom Pick Point tool plugin.",
                        "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to activate pick tool: {ex.Message}",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PickPointTool_PointPicked(Point3D point, ModelItem item)
        {
            PickPointTool.PointPicked -= PickPointTool_PointPicked;

            if (InvokeRequired)
            {
                Invoke(new Action<Point3D, ModelItem>(PickPointTool_PointPicked), point, item);
                return;
            }

            txtOriginX.Text = point.X.ToString("F3");
            txtOriginY.Text = point.Y.ToString("F3");
            txtOriginZ.Text = point.Z.ToString("F3");

            _pickedX = point.X;
            _pickedY = point.Y;
            _pickedZ = point.Z;

            string name = "Element";
            string uniqueId = "None";
            if (item != null)
            {
                name = item.DisplayName ?? item.ClassDisplayName ?? "Element";
                uniqueId = GetModelItemUniqueId(item);
            }

            _originSelectionType = "Vertex";
            _selectedElementName = name;
            _selectedElementId = uniqueId;

            lblSelectedInfo.Text = $"✓ Picked vertex of \"{name}\" at ({point.X:F3}, {point.Y:F3}, {point.Z:F3})";
            lblSelectedInfo.ForeColor = UITheme.Color.Success;
        }

        private void PickElementCenterTool_ElementPicked(Point3D point, ModelItem item)
        {
            PickElementCenterTool.ElementPicked -= PickElementCenterTool_ElementPicked;

            if (InvokeRequired)
            {
                Invoke(new Action<Point3D, ModelItem>(PickElementCenterTool_ElementPicked), point, item);
                return;
            }

            txtOriginX.Text = point.X.ToString("F3");
            txtOriginY.Text = point.Y.ToString("F3");
            txtOriginZ.Text = point.Z.ToString("F3");

            _pickedX = point.X;
            _pickedY = point.Y;
            _pickedZ = point.Z;

            string name = "Element";
            string uniqueId = "None";
            if (item != null)
            {
                name = item.DisplayName ?? item.ClassDisplayName ?? "Element";
                uniqueId = GetModelItemUniqueId(item);
            }

            _originSelectionType = "Element Center";
            _selectedElementName = name;
            _selectedElementId = uniqueId;

            lblSelectedInfo.Text = $"✓ Picked center of \"{name}\" at ({point.X:F3}, {point.Y:F3}, {point.Z:F3})";
            lblSelectedInfo.ForeColor = UITheme.Color.Success;
        }

        private void PickTool_Cancelled()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(PickTool_Cancelled));
                return;
            }

            PickPointTool.PointPicked -= PickPointTool_PointPicked;
            PickPointTool.SelectionCancelled -= PickTool_Cancelled;
            PickElementCenterTool.ElementPicked -= PickElementCenterTool_ElementPicked;
            PickElementCenterTool.SelectionCancelled -= PickTool_Cancelled;

            // Clear overlays instantly on cancel
            PickPointTool.ClearActiveOverlay();
            PickElementCenterTool.ClearActiveOverlay();

            lblSelectedInfo.Text = "ℹ Selection cancelled.";
            lblSelectedInfo.ForeColor = UITheme.Color.TextTertiary;
        }

        private void RegisterMouseMoveRecursive(Control control)
        {
            control.MouseMove += FormOrControl_MouseMove;
            foreach (Control child in control.Controls)
            {
                RegisterMouseMoveRecursive(child);
            }
        }

        private void FormOrControl_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc != null && doc.Tool.Value == Tool.CustomToolPlugin)
                {
                    PickPointTool.ClearActiveOverlay();
                    PickElementCenterTool.ClearActiveOverlay();
                }
            }
            catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc != null && doc.Tool.Value == Tool.CustomToolPlugin)
                {
                    // Restore to standard selection tool
                    doc.Tool.Value = Tool.Select;

                    // Trigger local cleanup and UI reset
                    PickTool_Cancelled();

                    return true; // Suppress normal Escape close behavior
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            // Parse inputs
            if (!double.TryParse(txtOriginX.Text, out double ox))
            {
                MessageBox.Show("Please enter a valid numeric value for Origin X.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtOriginX.Focus();
                return;
            }
            if (!double.TryParse(txtOriginY.Text, out double oy))
            {
                MessageBox.Show("Please enter a valid numeric value for Origin Y.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtOriginY.Focus();
                return;
            }
            if (!double.TryParse(txtOriginZ.Text, out double oz))
            {
                MessageBox.Show("Please enter a valid numeric value for Origin Z.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtOriginZ.Focus();
                return;
            }

            int mergeDepth = (int)numMergeDepth.Value;

            // Cache inputs statically
            _cachedMergeDepth = mergeDepth;
            _cachedOriginX = ox;
            _cachedOriginY = oy;
            _cachedOriginZ = oz;

            // Serialize group-by properties
            var groupBy = new System.Collections.Generic.List<string[]>();
            foreach (Control ctrl in flpProperties.Controls)
            {
                if (ctrl is Panel panel && panel.Controls.Count >= 2)
                {
                    if (panel.Controls[0] is ComboBox cbCat && panel.Controls[1] is ComboBox cbProp)
                    {
                        string cat = cbCat.Text.Trim();
                        string prop = cbProp.Text.Trim();
                        if (!string.IsNullOrEmpty(cat) && !string.IsNullOrEmpty(prop))
                        {
                            groupBy.Add(new string[] { cat, prop });
                        }
                    }
                }
            }
            _cachedGroupByProperties = groupBy;

            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                MessageBox.Show("Please open a Navisworks document before exporting.",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                System.Windows.Forms.Cursor.Current = Cursors.WaitCursor;
                btnExport.Enabled = false;
                btnCancel.Enabled = false;

                // Validate if manually edited coords still match picked coords
                bool coordsMatchPicked = Math.Abs(ox - _pickedX) < 0.001 &&
                                         Math.Abs(oy - _pickedY) < 0.001 &&
                                         Math.Abs(oz - _pickedZ) < 0.001;

                string finalSelectionType = coordsMatchPicked ? _originSelectionType : "Default / Manual";
                string finalElementName = coordsMatchPicked ? _selectedElementName : "None";
                string finalElementId = coordsMatchPicked ? _selectedElementId : "None";

                // Run optimized export (Epic's plugin will show the single SaveFileDialog natively)
                bool success = DatasmithExporterService.ExportActiveDocument(
                    doc, 
                    null, 
                    mergeDepth, 
                    ox, 
                    oy, 
                    oz,
                    finalSelectionType,
                    finalElementName,
                    finalElementId,
                    groupBy);

                System.Windows.Forms.Cursor.Current = Cursors.Default;
                btnExport.Enabled = true;
                btnCancel.Enabled = true;

                if (success)
                {
                    MessageBox.Show("Datasmith export completed successfully!",
                        "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    MessageBox.Show("Datasmith export failed. Please check log details.",
                        "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.Cursor.Current = Cursors.Default;
                btnExport.Enabled = true;
                btnCancel.Enabled = true;

                MessageBox.Show($"An error occurred during export:\n{ex.Message}",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CurrentSelection_Changed(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                Invoke(new System.EventHandler(CurrentSelection_Changed), sender, e);
                return;
            }
            UpdateSelectionInfo();
        }

        private static string GetModelItemUniqueId(ModelItem item)
        {
            if (item == null) return "None";
            try
            {
                if (item.InstanceGuid != Guid.Empty)
                {
                    return item.InstanceGuid.ToString();
                }

                foreach (var category in item.PropertyCategories)
                {
                    foreach (var property in category.Properties)
                    {
                        string propName = property.DisplayName;
                        if (string.Equals(propName, "GUID", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(propName, "IfcGUID", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(propName, "UniqueId", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(propName, "Element ID", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(propName, "Id", StringComparison.OrdinalIgnoreCase))
                        {
                            var val = property.Value;
                            if (val != null)
                            {
                                string valStr = val.ToString();
                                if (!string.IsNullOrEmpty(valStr))
                                {
                                    return valStr;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return "Hash_" + item.GetHashCode().ToString();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                PickPointTool.PointPicked -= PickPointTool_PointPicked;
                PickPointTool.SelectionCancelled -= PickTool_Cancelled;
                PickElementCenterTool.ElementPicked -= PickElementCenterTool_ElementPicked;
                PickElementCenterTool.SelectionCancelled -= PickTool_Cancelled;
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc != null && doc.CurrentSelection != null)
                {
                    doc.CurrentSelection.Changed -= CurrentSelection_Changed;
                }
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}
