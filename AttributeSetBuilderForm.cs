using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;

namespace Virtuart4DNavisworks
{
    public class AttributeSetBuilderForm : Form
    {
        private readonly Document _doc;
        private List<AttributePropertyInfo> _properties = new List<AttributePropertyInfo>();
        private bool _updatingControls;

        private ComboBox _cmbCategory;
        private ComboBox _cmbProperty;
        private RadioButton _rbSet;
        private RadioButton _rbSearch;
        private Button _btnBuild;
        private Button _btnRefresh;
        private ListView _lvResults;
        private Label _lblStatus;

        public AttributeSetBuilderForm(Document doc)
        {
            _doc = doc;
            _properties = AttributeSetBuilderService.DiscoverProperties(_doc);

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            InitializeUi();
            PopulateCategories();
            UpdateProperties();
        }

        private void InitializeUi()
        {
            Text = "Virtuart4D - Group Sets by Attribute";
            Size = new Size(760, 620);
            MinimumSize = new Size(680, 540);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UITheme.Color.Background;
            Font = UITheme.Typography.Body;

            var header = UITheme.CreateHeader(
                "Group Sets by Attribute",
                "Choose a category/property like Datasmith Smart Merging, then create Selection Sets or dynamic Search Sets.");
            Controls.Add(header);

            var footer = UITheme.CreateFooter();

            _btnBuild = UITheme.CreatePrimaryButton("Create Sets");
            _btnBuild.Width = 130;
            _btnBuild.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _btnBuild.Location = new Point(footer.Width - 150, 8);
            _btnBuild.Click += BtnBuild_Click;
            footer.Controls.Add(_btnBuild);

            var btnClose = UITheme.CreateSecondaryButton("Close");
            btnClose.Width = 100;
            btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            btnClose.Location = new Point(footer.Width - 265, 8);
            btnClose.Click += (s, e) => Close();
            footer.Controls.Add(btnClose);

            Controls.Add(footer);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(UITheme.Spacing.LG)
            };
            Controls.Add(body);
            body.BringToFront();

            var card = UITheme.CreateCard();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(UITheme.Spacing.LG);
            body.Controls.Add(card);

            int y = 0;

            var lblSection = new Label
            {
                Text = "ATTRIBUTE",
                Font = UITheme.Typography.Label,
                ForeColor = UITheme.Color.Primary,
                Location = new Point(0, y),
                AutoSize = true
            };
            card.Controls.Add(lblSection);
            y += 24;

            var lblHint = new Label
            {
                Text = "Sets are created under Selection Sets > [Category] > [Property]. Existing folders are reused.",
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextSecondary,
                Location = new Point(0, y),
                Size = new Size(650, 34)
            };
            card.Controls.Add(lblHint);
            y += 46;

            var lblCategory = new Label
            {
                Text = "Category:",
                Font = UITheme.Typography.Label,
                ForeColor = UITheme.Color.TextPrimary,
                Location = new Point(0, y + 6),
                AutoSize = true
            };
            card.Controls.Add(lblCategory);

            _cmbCategory = CreateComboBox();
            _cmbCategory.Location = new Point(92, y);
            _cmbCategory.Width = 560;
            _cmbCategory.TextChanged += (s, e) =>
            {
                if (!_updatingControls)
                    UpdateProperties();
            };
            card.Controls.Add(_cmbCategory);

            y += 38;

            var lblProperty = new Label
            {
                Text = "Property:",
                Font = UITheme.Typography.Label,
                ForeColor = UITheme.Color.TextPrimary,
                Location = new Point(0, y + 6),
                AutoSize = true
            };
            card.Controls.Add(lblProperty);

            _cmbProperty = CreateComboBox();
            _cmbProperty.Location = new Point(92, y);
            _cmbProperty.Width = 560;
            card.Controls.Add(_cmbProperty);

            y += 48;

            var modePanel = new Panel
            {
                Location = new Point(0, y),
                Size = new Size(660, 54),
                BackColor = UITheme.Color.BackgroundSecondary
            };
            card.Controls.Add(modePanel);

            var lblMode = new Label
            {
                Text = "Set type:",
                Font = UITheme.Typography.Label,
                ForeColor = UITheme.Color.TextPrimary,
                Location = new Point(12, 16),
                AutoSize = true
            };
            modePanel.Controls.Add(lblMode);

            _rbSet = new RadioButton
            {
                Text = "Set (snapshot of current elements)",
                Font = UITheme.Typography.Body,
                Location = new Point(96, 14),
                AutoSize = true,
                Checked = true
            };
            modePanel.Controls.Add(_rbSet);

            _rbSearch = new RadioButton
            {
                Text = "Search (dynamic rule)",
                Font = UITheme.Typography.Body,
                Location = new Point(330, 14),
                AutoSize = true
            };
            modePanel.Controls.Add(_rbSearch);

            y += 72;

            _btnRefresh = UITheme.CreateSecondaryButton("Refresh Attributes");
            _btnRefresh.Width = 150;
            _btnRefresh.Location = new Point(0, y);
            _btnRefresh.Click += (s, e) => RefreshAttributes();
            card.Controls.Add(_btnRefresh);

            _lblStatus = new Label
            {
                Text = "Ready.",
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextSecondary,
                Location = new Point(170, y + 8),
                Size = new Size(480, 22),
                AutoEllipsis = true
            };
            card.Controls.Add(_lblStatus);

            y += 48;

            var lblResults = new Label
            {
                Text = "RESULTS",
                Font = UITheme.Typography.Label,
                ForeColor = UITheme.Color.Primary,
                Location = new Point(0, y),
                AutoSize = true
            };
            card.Controls.Add(lblResults);
            y += 24;

            _lvResults = new ListView
            {
                Location = new Point(0, y),
                Size = new Size(660, 330),
                View = System.Windows.Forms.View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                Font = UITheme.Typography.Body
            };
            _lvResults.Columns.Add("Value", 170);
            _lvResults.Columns.Add("Items", 70, HorizontalAlignment.Right);
            _lvResults.Columns.Add("Mode", 80);
            _lvResults.Columns.Add("Set", 220);
            _lvResults.Columns.Add("Status", 110);
            card.Controls.Add(_lvResults);

            AcceptButton = _btnBuild;
            CancelButton = btnClose;
        }

        private ComboBox CreateComboBox()
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Font = UITheme.Typography.Body,
                BackColor = UITheme.Color.Surface,
                ForeColor = UITheme.Color.TextPrimary
            };
            return combo;
        }

        private void PopulateCategories()
        {
            _updatingControls = true;

            var current = _cmbCategory?.Text ?? string.Empty;
            _cmbCategory.Items.Clear();

            var categories = _properties
                .Select(p => p.Category)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cmbCategory.Items.AddRange(categories.Cast<object>().ToArray());

            if (categories.Any(name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase)))
                _cmbCategory.Text = current;
            else if (categories.Count > 0)
                _cmbCategory.Text = categories[0];

            _updatingControls = false;
        }

        private void UpdateProperties()
        {
            if (_cmbCategory == null || _cmbProperty == null || _updatingControls)
                return;

            _updatingControls = true;

            var current = _cmbProperty.Text;
            var category = _cmbCategory.Text;
            _cmbProperty.Items.Clear();

            var properties = _properties
                .Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Property)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _cmbProperty.Items.AddRange(properties.Cast<object>().ToArray());

            if (properties.Any(name => string.Equals(name, current, StringComparison.OrdinalIgnoreCase)))
                _cmbProperty.Text = current;
            else if (properties.Count > 0)
                _cmbProperty.Text = properties[0];

            _updatingControls = false;
        }

        private void RefreshAttributes()
        {
            try
            {
                System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
                _properties = AttributeSetBuilderService.DiscoverProperties(_doc);
                PopulateCategories();
                UpdateProperties();
                _lblStatus.Text = "Attributes refreshed.";
                _lblStatus.ForeColor = UITheme.Color.Success;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Refresh failed: " + ex.Message;
                _lblStatus.ForeColor = UITheme.Color.Error;
            }
            finally
            {
                System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
            }
        }

        private void BtnBuild_Click(object sender, EventArgs e)
        {
            var category = (_cmbCategory?.Text ?? string.Empty).Trim();
            var property = (_cmbProperty?.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(property))
            {
                MessageBox.Show("Choose a category and property before creating sets.",
                    "Virtuart4D - Group Sets", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var mode = _rbSearch.Checked ? AttributeSetMode.SearchSet : AttributeSetMode.SelectionSet;
            var progress = new Progress<string>(message => _lblStatus.Text = message);

            try
            {
                System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
                _btnBuild.Enabled = false;
                _btnRefresh.Enabled = false;
                _cmbCategory.Enabled = false;
                _cmbProperty.Enabled = false;
                _rbSet.Enabled = false;
                _rbSearch.Enabled = false;

                _lblStatus.Text = "Creating sets...";
                _lblStatus.ForeColor = UITheme.Color.TextSecondary;

                var results = AttributeSetBuilderService.Build(_doc, category, property, mode, progress);
                ShowResults(results);

                if (results.Count == 0)
                {
                    MessageBox.Show("No elements were found with values for this attribute.",
                        "Virtuart4D - Group Sets", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    var created = results.Count(r => r.Created);
                    var reused = results.Count(r => r.Reused);
                    var skipped = results.Count(r => r.Skipped);
                    MessageBox.Show($"Created: {created}\nUpdated: {reused}\nSkipped: {skipped}",
                        "Virtuart4D - Group Sets", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Failed: " + ex.Message;
                _lblStatus.ForeColor = UITheme.Color.Error;
                MessageBox.Show($"An error occurred while creating attribute sets:\n{ex.Message}",
                    "Virtuart4D - Group Sets", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnBuild.Enabled = true;
                _btnRefresh.Enabled = true;
                _cmbCategory.Enabled = true;
                _cmbProperty.Enabled = true;
                _rbSet.Enabled = true;
                _rbSearch.Enabled = true;
                System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.Default;
            }
        }

        private void ShowResults(List<AttributeSetBuildResult> results)
        {
            _lvResults.Items.Clear();

            foreach (var result in results)
            {
                var item = new ListViewItem(result.Value);
                item.SubItems.Add(result.ItemCount.ToString());
                item.SubItems.Add(result.Mode);
                item.SubItems.Add(result.SetName);
                item.SubItems.Add(GetResultStatus(result));
                item.Tag = result;
                _lvResults.Items.Add(item);
            }
        }

        private static string GetResultStatus(AttributeSetBuildResult result)
        {
            if (result.Created)
                return "Created";
            if (result.Reused)
                return "Updated";
            if (result.Skipped)
                return "Skipped";
            return result.Message ?? string.Empty;
        }
    }
}
