using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;

namespace Virtuart4DNavisworks
{
    public class BatchSetExportForm : Form
    {
        private readonly Document _doc;
        private readonly List<SetCacheEntry> _sets;
        private readonly HashSet<string> _setsSelecionados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ListView _lvSets;
        private TextBox _txtSearch;
        private Label _lblSelectedCount;
        private Label _lblOutputFolder;
        private NumericUpDown _numMergeDepth;
        private NumericUpDown _numOriginX;
        private NumericUpDown _numOriginY;
        private NumericUpDown _numOriginZ;
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Button _btnExport;
        private bool _atualizandoLista;
        private string _outputFolder;
        private bool _exporting;
        private bool _cancelRequested;

        private Queue<string> _exportQueue;
        private List<BatchSetExportResult> _exportResults;
        private Timer _exportTimer;
        private int _totalToExport;
        private int _exportedCount;
        private Selection _originalSelection;
        private List<ModelItem> _originalHidden;
        private List<ModelItem> _allItems;
        private ModelItemCollection _allItemsCollection;

        public BatchSetExportForm(Document doc)
        {
            _doc = doc;
            _sets = SelectionSetCache.Collect(doc)
                .Where(setInfo => !string.IsNullOrWhiteSpace(setInfo?.Nome))
                .OrderBy(setInfo => setInfo.Nome, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _outputFolder = !string.IsNullOrWhiteSpace(doc.FileName)
                ? Path.GetDirectoryName(doc.FileName)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (string.IsNullOrWhiteSpace(_outputFolder))
                _outputFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            MontarLayout();
            PreencherSets();
        }

        private void MontarLayout()
        {
            Text = "Virtuart4D - Batch Export Sets";
            Size = new Size(860, 680);
            MinimumSize = new Size(760, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UITheme.Color.Background;
            Font = UITheme.Typography.Body;

            var pnlHeader = UITheme.CreateHeader("Batch Export Sets", "Write Virtuart_Sets attribute and export each selected set as a fully merged Datasmith file.");
            pnlHeader.Height = 96;
            Controls.Add(pnlHeader);

            var pnlFooter = UITheme.CreateFooter();
            var btnCancel = UITheme.CreateSecondaryButton("Cancel");
            btnCancel.Width = 90;
            btnCancel.Location = new Point(pnlFooter.Width - 190, 8);
            btnCancel.Click += (s, e) =>
            {
                if (_exporting)
                {
                    _cancelRequested = true;
                    _lblStatus.Text = "Cancelling export... Please wait.";
                }
                else
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            pnlFooter.Controls.Add(btnCancel);

            _btnExport = UITheme.CreatePrimaryButton("Export Batch");
            _btnExport.Width = 120;
            _btnExport.Location = new Point(pnlFooter.Width - 60, 8);
            _btnExport.Click += BtnExport_Click;
            pnlFooter.Controls.Add(_btnExport);
            Controls.Add(pnlFooter);

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

            int y = 0;
            var lblTitle = new Label
            {
                Text = "Selection Sets",
                Font = UITheme.Typography.H3,
                ForeColor = UITheme.Color.Primary,
                AutoSize = true,
                Location = new Point(0, y)
            };
            card.Controls.Add(lblTitle);
            y += 22;

            var lblHint = new Label
            {
                Text = "For each selected set: writes the set name to all elements, temporarily hides everything else, and exports with full merge depth.",
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextSecondary,
                AutoSize = true,
                Location = new Point(0, y)
            };
            card.Controls.Add(lblHint);
            y += 24;

            var pnlSearch = new Panel
            {
                Dock = DockStyle.None,
                Width = 300,
                Height = 28,
                Location = new Point(0, y)
            };
            _txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextTertiary,
                Text = "Search sets...",
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtSearch.GotFocus += (s, e) =>
            {
                if (_txtSearch.ForeColor == UITheme.Color.TextTertiary)
                {
                    _txtSearch.Text = "";
                    _txtSearch.ForeColor = UITheme.Color.TextPrimary;
                }
            };
            _txtSearch.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_txtSearch.Text))
                {
                    _txtSearch.Text = "Search sets...";
                    _txtSearch.ForeColor = UITheme.Color.TextTertiary;
                }
            };
            _txtSearch.TextChanged += (s, e) => PreencherSets();
            pnlSearch.Controls.Add(_txtSearch);
            card.Controls.Add(pnlSearch);
            y += 34;

            _lvSets = new ListView
            {
                Location = new Point(0, y),
                Width = 780,
                Height = 330,
                View = System.Windows.Forms.View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.FixedSingle,
                Font = UITheme.Typography.BodySmall,
                GridLines = false,
                MultiSelect = false,
                ShowItemToolTips = true
            };
            _lvSets.Columns.Add("Set Name", 560);
            _lvSets.Columns.Add("Elements", 90, HorizontalAlignment.Center);
            _lvSets.ItemChecked += LvSets_ItemChecked;
            card.Controls.Add(_lvSets);
            y += 338;

            var pnlSetButtons = new FlowLayoutPanel
            {
                Location = new Point(0, y),
                Width = 780,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            var btnSelectAll = UITheme.CreateSecondaryButton("Select All");
            btnSelectAll.Width = 86;
            btnSelectAll.Height = 26;
            btnSelectAll.Click += (s, e) => DefinirSelecao(true);

            var btnClear = UITheme.CreateSecondaryButton("Clear");
            btnClear.Width = 70;
            btnClear.Height = 26;
            btnClear.Click += (s, e) => DefinirSelecao(false);

            _lblSelectedCount = new Label
            {
                AutoSize = true,
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextSecondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(12, 8, 0, 0)
            };

            pnlSetButtons.Controls.Add(btnSelectAll);
            pnlSetButtons.Controls.Add(btnClear);
            pnlSetButtons.Controls.Add(_lblSelectedCount);
            card.Controls.Add(pnlSetButtons);
            y += 42;

            var btnChooseFolder = UITheme.CreatePrimaryButton("Choose Folder");
            btnChooseFolder.Width = 120;
            btnChooseFolder.Height = 28;
            btnChooseFolder.Click += BtnChooseFolder_Click;
            card.Controls.Add(btnChooseFolder);

            _lblOutputFolder = new Label
            {
                AutoSize = true,
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextPrimary,
                Location = new Point(132, y + 5),
                MaximumSize = new Size(620, 20),
                AutoEllipsis = true,
                Text = _outputFolder
            };
            card.Controls.Add(_lblOutputFolder);
            y += 34;

            var pnlOptions = new TableLayoutPanel
            {
                Location = new Point(0, y),
                Width = 780,
                Height = 92,
                ColumnCount = 4,
                RowCount = 3,
                BackColor = System.Drawing.Color.Transparent
            };
            pnlOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            pnlOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            pnlOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            pnlOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddOptionLabel(pnlOptions, 0, 0, "Merge Depth");
            _numMergeDepth = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 50,
                Value = BatchSetExportService.DefaultFullMergeDepth,
                Font = UITheme.Typography.BodySmall,
                Width = 100
            };
            pnlOptions.Controls.Add(_numMergeDepth, 1, 0);

            AddOptionLabel(pnlOptions, 0, 1, "Origin X");
            _numOriginX = new NumericUpDown
            {
                Minimum = -100000,
                Maximum = 100000,
                DecimalPlaces = 3,
                Value = 0,
                Font = UITheme.Typography.BodySmall,
                Width = 100
            };
            pnlOptions.Controls.Add(_numOriginX, 1, 1);

            AddOptionLabel(pnlOptions, 2, 1, "Origin Y");
            _numOriginY = new NumericUpDown
            {
                Minimum = -100000,
                Maximum = 100000,
                DecimalPlaces = 3,
                Value = 0,
                Font = UITheme.Typography.BodySmall,
                Width = 100
            };
            pnlOptions.Controls.Add(_numOriginY, 3, 1);

            AddOptionLabel(pnlOptions, 2, 2, "Origin Z");
            _numOriginZ = new NumericUpDown
            {
                Minimum = -100000,
                Maximum = 100000,
                DecimalPlaces = 3,
                Value = 0,
                Font = UITheme.Typography.BodySmall,
                Width = 100
            };
            pnlOptions.Controls.Add(_numOriginZ, 3, 2);

            card.Controls.Add(pnlOptions);
            y += 104;

            _progressBar = new ProgressBar
            {
                Location = new Point(0, y),
                Width = 780,
                Height = 18,
                Minimum = 0,
                Maximum = 1
            };
            card.Controls.Add(_progressBar);
            y += 26;

            _lblStatus = new Label
            {
                Location = new Point(0, y),
                Width = 780,
                Height = 42,
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextSecondary,
                AutoEllipsis = true
            };
            card.Controls.Add(_lblStatus);

            AcceptButton = _btnExport;
            CancelButton = btnCancel;
            AtualizarEstadoExport();
        }

        private void AddOptionLabel(TableLayoutPanel panel, int column, int row, string text)
        {
            panel.Controls.Add(new Label
            {
                Text = text,
                Font = UITheme.Typography.BodySmall,
                ForeColor = UITheme.Color.TextPrimary,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 0, 0)
            }, column, row);
        }

        private void BtnChooseFolder_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (!string.IsNullOrWhiteSpace(_outputFolder) && Directory.Exists(_outputFolder))
                    dlg.SelectedPath = _outputFolder;

                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;

                _outputFolder = dlg.SelectedPath;
                _lblOutputFolder.Text = _outputFolder;
                AtualizarEstadoExport();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_exporting)
            {
                e.Cancel = true;
                _cancelRequested = true;
                _lblStatus.Text = "Cancelling export... Please wait.";
                System.Windows.Forms.Application.DoEvents();
            }
            else
            {
                base.OnFormClosing(e);
            }
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            if (_setsSelecionados.Count == 0)
            {
                MessageBox.Show("Select at least one set before exporting.", "Virtuart4D - Batch Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_outputFolder) || !Directory.Exists(_outputFolder))
            {
                MessageBox.Show("Choose an output folder before exporting.", "Virtuart4D - Batch Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedSets = _setsSelecionados.ToList();
            _totalToExport = selectedSets.Count;
            _exportedCount = 0;
            _exportQueue = new Queue<string>(selectedSets);
            _exportResults = new List<BatchSetExportResult>();

            // Save original visibility and selection state before batch starts
            _originalSelection = new Selection(_doc.CurrentSelection);
            _originalHidden = _doc.Models.RootItemDescendantsAndSelf
                .Where(item => item.IsHidden)
                .ToList();
            _allItems = _doc.Models.RootItemDescendantsAndSelf.ToList();
            _allItemsCollection = ToModelItemCollection(_allItems);

            // Disable controls
            _btnExport.Enabled = false;
            _exporting = true;
            _cancelRequested = false;

            _progressBar.Minimum = 0;
            _progressBar.Maximum = _totalToExport;
            _progressBar.Value = 0;

            // Start deferred export timer to yield execution back to Navisworks
            _exportTimer = new Timer { Interval = 200 };
            _exportTimer.Tick += ExportTimer_Tick;
            _exportTimer.Start();
        }

        private void ExportTimer_Tick(object sender, EventArgs e)
        {
            _exportTimer.Stop(); // Prevent overlapping ticks

            if (_cancelRequested)
            {
                FinalizarBatch(true);
                return;
            }

            if (_exportQueue.Count == 0)
            {
                FinalizarBatch(false);
                return;
            }

            string setName = _exportQueue.Dequeue();
            _progressBar.Value = _exportedCount;
            _lblStatus.Text = $"Exporting set ({_exportedCount + 1}/{_totalToExport}): {setName}";
            System.Windows.Forms.Application.DoEvents();

            try
            {
                var result = BatchSetExportService.ExportSingleSet(
                    _doc,
                    setName,
                    _outputFolder,
                    (int)_numMergeDepth.Value,
                    (double)_numOriginX.Value,
                    (double)_numOriginY.Value,
                    (double)_numOriginZ.Value,
                    _allItems);

                _exportResults.Add(result);
            }
            catch (Exception ex)
            {
                _exportResults.Add(new BatchSetExportResult
                {
                    SetName = setName,
                    ElementCount = 0,
                    OutputPath = "",
                    Success = false,
                    Message = ex.Message
                });
            }

            _exportedCount++;
            _progressBar.Value = _exportedCount;

            // Schedule the next export after 800ms to allow Navisworks to process its idle queue and flush file locks
            if (_exportQueue.Count > 0 && !_cancelRequested)
            {
                _exportTimer.Interval = 800;
                _exportTimer.Start();
            }
            else
            {
                FinalizarBatch(_cancelRequested);
            }
        }

        private void FinalizarBatch(bool cancelled)
        {
            if (_exportTimer != null)
            {
                _exportTimer.Stop();
                _exportTimer.Tick -= ExportTimer_Tick;
                _exportTimer.Dispose();
                _exportTimer = null;
            }

            // Restore original visibility and selection state ONCE at the end of the batch
            try
            {
                var originalHiddenCollection = ToModelItemCollection(_originalHidden);
                _doc.Models.SetHidden(_allItemsCollection, false);
                _doc.Models.SetHidden(originalHiddenCollection, true);
                _doc.CurrentSelection.CopyFrom(_originalSelection);
            }
            catch
            {
                // Ignore restore failures
            }

            _progressBar.Value = _progressBar.Maximum;
            _lblStatus.Text = cancelled ? "Export cancelled." : "Export completed.";
            _exporting = false;
            _btnExport.Enabled = true;

            ShowSummary(_exportResults, cancelled);
        }

        private static ModelItemCollection ToModelItemCollection(IEnumerable<ModelItem> items)
        {
            var collection = new ModelItemCollection();
            collection.AddRange(items);
            return collection;
        }

        private void ShowSummary(List<BatchSetExportResult> results, bool cancelled)
        {
            var success = results.Count(r => r.Success);
            var failed = results.Count(r => !r.Success);
            var warnings = results.Count(r => r.HasAttributeWarning);
            var sb = new StringBuilder();
            
            if (cancelled)
                sb.AppendLine("Batch export cancelled by user.");
            else
                sb.AppendLine("Batch export completed.");

            sb.AppendLine($"Exported: {success}");
            sb.AppendLine($"Failed: {failed}");
            sb.AppendLine($"Attribute warnings: {warnings}");
            sb.AppendLine();
            sb.AppendLine($"Output folder:");
            sb.AppendLine(_outputFolder);

            var failures = results.Where(r => !r.Success).Take(5).ToList();
            if (failures.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Failures:");
                foreach (var result in failures)
                    sb.AppendLine($"- {result.SetName}: {result.Message}");
            }

            var icon = failed == 0 && warnings == 0
                ? MessageBoxIcon.Information
                : failed == results.Count
                    ? MessageBoxIcon.Error
                    : MessageBoxIcon.Warning;

            MessageBox.Show(sb.ToString(), "Virtuart4D - Batch Export", MessageBoxButtons.OK, icon);
            DialogResult = cancelled ? DialogResult.Cancel : DialogResult.OK;
            Close();
        }

        private void PreencherSets()
        {
            _atualizandoLista = true;
            _lvSets.Items.Clear();

            var filtro = "";
            if (!string.IsNullOrWhiteSpace(_txtSearch.Text) && _txtSearch.ForeColor != UITheme.Color.TextTertiary)
                filtro = _txtSearch.Text.Trim().ToLower();

            foreach (var setInfo in _sets)
            {
                if (!string.IsNullOrEmpty(filtro) && !setInfo.Nome.ToLower().Contains(filtro))
                    continue;

                var item = new ListViewItem(setInfo.Nome)
                {
                    Tag = setInfo,
                    ToolTipText = $"{setInfo.Itens} element(s) belong to this set.",
                    Checked = _setsSelecionados.Contains(setInfo.Nome)
                };
                item.SubItems.Add(setInfo.Itens.ToString());
                _lvSets.Items.Add(item);
            }

            _atualizandoLista = false;
            AtualizarResumo();
            AtualizarEstadoExport();
        }

        private void LvSets_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_atualizandoLista || !(e.Item.Tag is SetCacheEntry setInfo))
                return;

            if (e.Item.Checked)
                _setsSelecionados.Add(setInfo.Nome);
            else
                _setsSelecionados.Remove(setInfo.Nome);

            AtualizarResumo();
            AtualizarEstadoExport();
        }

        private void DefinirSelecao(bool selecionarTodos)
        {
            _setsSelecionados.Clear();
            if (selecionarTodos)
            {
                foreach (var setInfo in _sets)
                    _setsSelecionados.Add(setInfo.Nome);
            }

            PreencherSets();
        }

        private void AtualizarResumo()
        {
            _lblSelectedCount.Text = $"{_setsSelecionados.Count} selected / {_sets.Count} sets";
        }

        private void AtualizarEstadoExport()
        {
            if (_btnExport == null)
                return;

            _btnExport.Enabled = _setsSelecionados.Count > 0 &&
                                 !string.IsNullOrWhiteSpace(_outputFolder) &&
                                 Directory.Exists(_outputFolder);
        }
    }
}
