using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
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
                LargeImage  = CriarIcone("E", System.Drawing.Color.FromArgb(108, 92, 168)),
                Image       = CriarIcone16("E", System.Drawing.Color.FromArgb(108, 92, 168))
            };
            btnExport.CommandHandler = new Virtuart4DCommandHandler();

            panelSource.Items.Add(btnExport);

            var panel = new RibbonPanel { Source = panelSource };
            tab.Panels.Add(panel);

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

            if (_settingsForm == null || _settingsForm.IsDisposed)
            {
                _settingsForm = new ExportSettingsForm();
                
                // Get Navisworks main window handle as owner so it floats on top without blocking
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
        }
    }
}
