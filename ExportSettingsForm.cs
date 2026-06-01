using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
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

        private NumericUpDown numMergeDepth;
        private TextBox txtOriginX;
        private TextBox txtOriginY;
        private TextBox txtOriginZ;
        private Button btnPickSelected;
        private Button btnResetOrigin;
        private Button btnExport;
        private Button btnCancel;
        private Label lblSelectedInfo;

        public ExportSettingsForm()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            InitializeUI();
        }

        private void InitializeUI()
        {
            Text = "Virtuart4D - Export Settings";
            Size = new Size(520, 560);
            MinimumSize = new Size(520, 560);
            MaximumSize = new Size(520, 560);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
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

            btnExport = UITheme.CreateSuccessButton("Export");
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
            btnPickSelected = UITheme.CreatePrimaryButton("Pick Selection Center");
            btnPickSelected.Width = 170;
            btnPickSelected.Location = new Point(UITheme.Spacing.LG, y);
            btnPickSelected.Click += BtnPickSelected_Click;
            card.Controls.Add(btnPickSelected);

            btnResetOrigin = UITheme.CreateSecondaryButton("Reset to Zero");
            btnResetOrigin.Width = 110;
            btnResetOrigin.Location = new Point(UITheme.Spacing.LG + 185, y);
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

        private void BtnPickSelected_Click(object sender, EventArgs e)
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null || doc.CurrentSelection == null || doc.CurrentSelection.SelectedItems.IsEmpty)
            {
                MessageBox.Show("Please select an element or group in Navisworks first.",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

                    string label = doc.CurrentSelection.SelectedItems.Count == 1 
                        ? doc.CurrentSelection.SelectedItems[0].DisplayName 
                        : $"{doc.CurrentSelection.SelectedItems.Count} elements";

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
            lblSelectedInfo.Text = "Origin coordinates reset to (0,0,0).";
            lblSelectedInfo.ForeColor = UITheme.Color.TextSecondary;
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

            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                MessageBox.Show("Please open a Navisworks document before exporting.",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Export to Unreal Engine Datasmith";
                sfd.Filter = "Unreal Datasmith (*.udatasmith)|*.udatasmith";
                sfd.DefaultExt = "udatasmith";

                // Suggest current file name
                if (!string.IsNullOrEmpty(doc.FileName))
                {
                    sfd.FileName = Path.GetFileNameWithoutExtension(doc.FileName);
                }

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        System.Windows.Forms.Cursor.Current = Cursors.WaitCursor;
                        btnExport.Enabled = false;
                        btnCancel.Enabled = false;

                        // Run optimized export with parameters
                        bool success = DatasmithExporterService.ExportActiveDocument(doc, sfd.FileName, mergeDepth, ox, oy, oz);

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
            }
        }
    }
}
