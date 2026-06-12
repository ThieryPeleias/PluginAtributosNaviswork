using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Windows;

namespace Virtuart4DNavisworks
{
    /// <summary>
    /// EventWatcherPlugin that creates the "Virtuart4D" tab in the Navisworks 2025 Ribbon.
    /// Uses the AdWindows.dll API for ribbon integration.
    /// </summary>
    [Plugin("Virtuart4DNavisworks.Exporter", "VT4D",
        DisplayName = "Virtuart4D Datasmith Exporter",
        ToolTip = "Export your Navisworks scene to Unreal Engine Datasmith format")]
    public class Virtuart4DPlugin : EventWatcherPlugin
    {
        private bool _ribbonCreated;

        public override void OnLoaded()
        {
            // Wait for the Ribbon to be initialized
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (s, e) =>
            {
                if (ComponentManager.Ribbon != null)
                {
                    timer.Stop();
                    timer.Dispose();
                    CriarRibbon();
                }
            };
            timer.Start();
        }

        public override void OnUnloading()
        {
        }

        private void CriarRibbon()
        {
            if (_ribbonCreated) return;
            _ribbonCreated = true;

            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;

            // Create Tab "Virtuart4D"
            var tab = new RibbonTab
            {
                Title = "Virtuart4D",
                Id    = "ID_VIRTUART4D_TAB",
                Name  = "Virtuart4D"
            };

            // Panel "Export"
            var panelSource = new RibbonPanelSource
            {
                Title = "Datasmith Exporter",
                Name  = "DatasmithExporter"
            };

            // Button: Export Datasmith
            var btnExport = new RibbonButton
            {
                Text        = "Export\nDatasmith",
                Id          = "ID_BTN_EXPORT_DATASMITH",
                Size        = RibbonItemSize.Large,
                ShowText    = true,
                ShowImage   = true,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage  = CriarIcone("E", System.Drawing.Color.FromArgb(0, 117, 134)),
                Image       = CriarIcone16("E", System.Drawing.Color.FromArgb(0, 117, 134))
            };
            btnExport.CommandHandler = new Virtuart4DCommandHandler();

            panelSource.Items.Add(btnExport);

            // Button: Batch Export Sets
            var btnBatchExport = new RibbonButton
            {
                Text        = "Batch\nExport Sets",
                Id          = "ID_BTN_BATCH_EXPORT_SETS",
                Size        = RibbonItemSize.Large,
                ShowText    = true,
                ShowImage   = true,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage  = CriarIcone("B", System.Drawing.Color.FromArgb(0, 117, 134)),
                Image       = CriarIcone16("B", System.Drawing.Color.FromArgb(0, 117, 134))
            };
            btnBatchExport.CommandHandler = new Virtuart4DCommandHandler();

            panelSource.Items.Add(btnBatchExport);

            var panel = new RibbonPanel { Source = panelSource };
            tab.Panels.Add(panel);

            // Panel "Attributes"
            var panelAttrSource = new RibbonPanelSource
            {
                Title = "Attributes",
                Name  = "Attributes"
            };

            // Button: Write Attribute
            var btnWriteAttribute = new RibbonButton
            {
                Text        = "Write\nAttribute",
                Id          = "ID_BTN_WRITE_ATTRIBUTE",
                Size        = RibbonItemSize.Large,
                ShowText    = true,
                ShowImage   = true,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage  = CriarIcone("W", System.Drawing.Color.FromArgb(0, 117, 134)),
                Image       = CriarIcone16("W", System.Drawing.Color.FromArgb(0, 117, 134))
            };
            btnWriteAttribute.CommandHandler = new Virtuart4DCommandHandler();

            panelAttrSource.Items.Add(btnWriteAttribute);

            var btnGroupSets = new RibbonButton
            {
                Text        = "Group\nSets by Attribute",
                Id          = "ID_BTN_GROUP_SETS_BY_ATTRIBUTE",
                Size        = RibbonItemSize.Large,
                ShowText    = true,
                ShowImage   = true,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage  = CriarIcone("G", System.Drawing.Color.FromArgb(0, 117, 134)),
                Image       = CriarIcone16("G", System.Drawing.Color.FromArgb(0, 117, 134))
            };
            btnGroupSets.CommandHandler = new Virtuart4DCommandHandler();

            panelAttrSource.Items.Add(btnGroupSets);

            var panelAttr = new RibbonPanel { Source = panelAttrSource };
            tab.Panels.Add(panelAttr);

            ribbon.Tabs.Add(tab);
            tab.IsActive = false;
        }

        // Programmatically generate icons
        private static BitmapImage CriarIcone(string letter, System.Drawing.Color color)
        {
            return GerarIconeBitmap(32, letter, color);
        }

        private static BitmapImage CriarIcone16(string letter, System.Drawing.Color color)
        {
            return GerarIconeBitmap(16, letter, color);
        }

        private static BitmapImage GerarIconeBitmap(int size, string letter, System.Drawing.Color color)
        {
            using (var bmp = new System.Drawing.Bitmap(size, size))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                // Rounded background
                using (var brush = new System.Drawing.SolidBrush(color))
                {
                    int r = size / 5;
                    var rect = new System.Drawing.Rectangle(0, 0, size - 1, size - 1);
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        int d = r * 2;
                        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                        path.CloseFigure();
                        g.FillPath(brush, path);
                    }
                }

                // Centralized text
                float fontSize = size * 0.55f;
                using (var font = new System.Drawing.Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold))
                using (var sf = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center })
                {
                    g.DrawString(letter, font, System.Drawing.Brushes.White,
                        new System.Drawing.RectangleF(0, 0, size, size), sf);
                }

                // Convert to BitmapImage for WPF
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
        }
    }

    /// <summary>
    /// Command handler for Virtuart4D Ribbon buttons.
    /// </summary>
    internal class Virtuart4DCommandHandler : System.Windows.Input.ICommand
    {
        public event EventHandler CanExecuteChanged { add { } remove { } }

        private static ExportSettingsForm _settingsForm;
        private static BatchSetExportForm _batchForm;

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            var btn = parameter as RibbonCommandItem;
            if (btn == null) return;

            if (btn.Id == "ID_BTN_EXPORT_DATASMITH")
            {
                ExecutarExportacao();
            }
            else if (btn.Id == "ID_BTN_BATCH_EXPORT_SETS")
            {
                ExecutarBatchExportSets();
            }
            else if (btn.Id == "ID_BTN_WRITE_ATTRIBUTE")
            {
                ExecutarGravacaoAtributo();
            }
            else if (btn.Id == "ID_BTN_GROUP_SETS_BY_ATTRIBUTE")
            {
                ExecutarGroupSetsByAttribute();
            }
        }

        private void ExecutarExportacao()
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                MessageBox.Show("Please open a Navisworks document before exporting.",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Defer showing the form by 50ms using a Timer to let AdWindows Ribbon complete its command scope,
            // which yields execution back to Autodesk Navisworks and fully unblocks the viewport.
            var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();

                if (_settingsForm == null || _settingsForm.IsDisposed)
                {
                    _settingsForm = new ExportSettingsForm();
                    
                    // Show modeless using Navisworks main window as owner so it floats on top without blocking
                    IWin32Window owner = Autodesk.Navisworks.Api.Application.Gui.MainWindow;
                    _settingsForm.Show(owner);
                }
                else
                {
                    _settingsForm.BringToFront();
                    if (_settingsForm.WindowState == FormWindowState.Minimized)
                    {
                        _settingsForm.WindowState = FormWindowState.Normal;
                    }
                }
            };
            timer.Start();
        }

        private void ExecutarBatchExportSets()
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                MessageBox.Show("Please open a Navisworks document before batch exporting sets.",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();

                try
                {
                    if (_batchForm == null || _batchForm.IsDisposed)
                    {
                        _batchForm = new BatchSetExportForm(doc);
                        IWin32Window owner = Autodesk.Navisworks.Api.Application.Gui.MainWindow;
                        _batchForm.Show(owner);
                    }
                    else
                    {
                        _batchForm.BringToFront();
                        if (_batchForm.WindowState == FormWindowState.Minimized)
                        {
                            _batchForm.WindowState = FormWindowState.Normal;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred while opening batch export:\n{ex.Message}",
                        "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            timer.Start();
        }

        private void ExecutarGravacaoAtributo()
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                MessageBox.Show("Please open a Navisworks document before writing attributes.",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selecao = doc.CurrentSelection.SelectedItems;
            if (selecao.Count == 0)
            {
                MessageBox.Show("Select at least one element in the model.",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();

                try
                {
                    var setsPorItem = MapearTodosSetsPorItem(doc, selecao);
                    var setsDetectados = CriarResumoSets(setsPorItem);
                    var setsSalvos = AtributoService.LerSetsSalvos(
                        selecao[0],
                        setsDetectados.Select(setInfo => setInfo.Nome),
                        VirtuartSchema.CategoriaPrincipal);

                    string nomeSetDetectado = setsPorItem.TryGetValue(selecao[0], out var setsPrimeiroItem) &&
                                              setsPrimeiroItem.Count > 0
                        ? setsPrimeiroItem[0].Nome
                        : null;

                    using (var form = new WriteAttributeForm(
                        selecao.Count,
                        setsDetectados,
                        nomeSetDetectado,
                        setsSalvos))
                    {
                        IWin32Window owner = Autodesk.Navisworks.Api.Application.Gui.MainWindow;
                        if (form.ShowDialog(owner) != DialogResult.OK) return;

                        var colecao = new ModelItemCollection();
                        foreach (var item in selecao)
                            colecao.Add(item);

                        var setsSelecionadosPorItem = FiltrarSetsSelecionados(setsPorItem, form.NomesSetsSelecionados);

                        var resultado = form.SolicitarExclusao
                            ? AtributoService.ExcluirAtributos(colecao, form.NomeCategoria)
                            : AtributoService.GravarAtributos(colecao, form.Atributos, form.NomeCategoria, setsSelecionadosPorItem);

                        MessageBox.Show(resultado.mensagem,
                            form.SolicitarExclusao ? "Virtuart4D - Delete" : "Virtuart4D - Save",
                            MessageBoxButtons.OK,
                            resultado.erros > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred during attribute write/delete:\n{ex.Message}",
                        "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            timer.Start();
        }

        private void ExecutarGroupSetsByAttribute()
        {
            var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                MessageBox.Show("Please open a Navisworks document before grouping sets.",
                    "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();

                try
                {
                    IWin32Window owner = Autodesk.Navisworks.Api.Application.Gui.MainWindow;
                    using (var form = new AttributeSetBuilderForm(doc))
                    {
                        form.ShowDialog(owner);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred while opening attribute set grouping:\n{ex.Message}",
                        "Virtuart4D Exporter", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            timer.Start();
        }

        private Dictionary<ModelItem, List<SetAssignment>> MapearTodosSetsPorItem(
            Document doc, IEnumerable<ModelItem> selecao)
        {
            var mapa = new Dictionary<ModelItem, List<SetAssignment>>();

            try
            {
                foreach (var item in selecao)
                    mapa[item] = new List<SetAssignment>();

                foreach (var setInfo in SelectionSetCache.Collect(doc))
                {
                    foreach (ModelItem itemDoSet in setInfo.Itens)
                    {
                        if (mapa.TryGetValue(itemDoSet, out var setsDoItem))
                            setsDoItem.Add(new SetAssignment(setInfo.NomeGravacao));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Virtuart4D] Erro ao mapear sets da selecao: {ex.Message}");
            }
            return mapa;
        }

        private List<WriteAttributeForm.SetResumo> CriarResumoSets(
            Dictionary<ModelItem, List<SetAssignment>> setsPorItem)
        {
            var itensPorNome = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var setsDoItem in setsPorItem.Values)
            {
                foreach (var nome in (setsDoItem ?? new List<SetAssignment>())
                    .Where(setInfo => !string.IsNullOrWhiteSpace(setInfo?.Nome))
                    .Select(setInfo => setInfo.Nome.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (itensPorNome.TryGetValue(nome, out var quantidade))
                        itensPorNome[nome] = quantidade + 1;
                    else
                        itensPorNome[nome] = 1;
                }
            }

            return itensPorNome
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new WriteAttributeForm.SetResumo(kvp.Key, kvp.Value))
                .ToList();
        }

        private Dictionary<ModelItem, List<SetAssignment>> FiltrarSetsSelecionados(
            Dictionary<ModelItem, List<SetAssignment>> setsPorItem,
            IEnumerable<string> nomesSelecionados)
        {
            if (setsPorItem == null)
                return null;

            var nomes = new HashSet<string>(
                (nomesSelecionados ?? Enumerable.Empty<string>())
                    .Where(nome => !string.IsNullOrWhiteSpace(nome))
                    .Select(nome => nome.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var resultado = new Dictionary<ModelItem, List<SetAssignment>>();
            foreach (var kvp in setsPorItem)
            {
                resultado[kvp.Key] = (kvp.Value ?? new List<SetAssignment>())
                    .Where(setInfo => !string.IsNullOrWhiteSpace(setInfo?.Nome) && nomes.Contains(setInfo.Nome.Trim()))
                    .GroupBy(setInfo => setInfo.Nome.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
            }

            return resultado;
        }
    }
}
