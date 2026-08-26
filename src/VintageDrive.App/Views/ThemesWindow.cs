#nullable disable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VintageDrive.App.Views
{
    /// <summary>Fenêtre des thèmes : défaut Vintage Players + import/export .vdtheme communautaires.</summary>
    public class ThemesWindow : Window
    {
        public ThemesWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            Content = Build();
        }

        private UIElement Build()
        {
            var body = new StackPanel { Width = 480 };
            body.Children.Add(Ui.P("▶ THÈMES", 14, "GoldBrush", bold: true));

            // ── Thème par défaut
            var def = new StackPanel();
            def.Children.Add(Ui.Spread(
                Ui.P("VINTAGE PLAYERS", 12, "BrightBrush", bold: true),
                ThemeEngine.CurrentName == "Vintage Players"
                    ? (UIElement)Ui.Badge("ACTIF", "GoldBrush", filled: true)
                    : MakeApplyButton()));
            var swatches = Ui.HStack(6,
                Swatch("#0A0A1E"), Swatch("#191938"), Swatch("#7B7BE8"),
                Swatch("#FFD23F"), Swatch("#FF2E97"), Swatch("#35E0E8"));
            swatches.Margin = new Thickness(0, 10, 0, 8);
            def.Children.Add(swatches);
            def.Children.Add(Ui.T("Thème par défaut — par le créateur de l'outil", 16, "LavenderBrush"));
            var defCard = Ui.Card(def, "GoldBrush");
            defCard.Margin = new Thickness(0, 14, 0, 14);
            body.Children.Add(defCard);

            // ── Thème actif si personnalisé
            if (ThemeEngine.CurrentName != "Vintage Players")
            {
                var cur = new StackPanel();
                cur.Children.Add(Ui.Spread(
                    Ui.P(ThemeEngine.CurrentName.ToUpperInvariant(), 12, "BrightBrush", bold: true),
                    Ui.Badge("ACTIF", "GoldBrush", filled: true)));
                cur.Children.Add(Ui.T("Thème communautaire importé", 16, "LavenderBrush"));
                var curCard = Ui.Card(cur, "PeriBrush");
                curCard.Margin = new Thickness(0, 0, 0, 14);
                body.Children.Add(curCard);
            }

            // ── Import / export
            var import = Ui.Btn("IMPORTER UN THÈME (.vdtheme)", "BtnOutline", 10);
            import.Click += (s, e) => Import();
            var export = Ui.Btn("EXPORTER LE THÈME ACTUEL", "BtnOutline", 10);
            export.Margin = new Thickness(12, 0, 0, 0);
            export.Click += (s, e) => Export();
            var row = Ui.HStack(0, import, export);
            row.Margin = new Thickness(0, 0, 0, 14);
            body.Children.Add(row);

            var note = Ui.T("Crée ton thème : exporte, ouvre le fichier, change les couleurs, réimporte. Partage-le à la scène — les meilleurs seront mis en avant dans les vidéos Vintage Players.", 15, "DimBrush", wrap: true);
            note.Margin = new Thickness(0, 0, 0, 14);
            body.Children.Add(note);

            var close = Ui.Btn("FERMER", "BtnGold", 11);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += (s, e) => Close();
            body.Children.Add(close);

            var frame = new Border
            {
                Background = Ui.B("PanelBrush"),
                BorderBrush = Ui.B("PeriBrush"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(24, 18, 24, 18),
                Margin = new Thickness(0, 0, 5, 5),
                Child = body,
            };
            var shadow = new Border { Background = Brushes.Black, Margin = new Thickness(5, 5, 0, 0) };
            var grid = new Grid();
            grid.Children.Add(shadow);
            grid.Children.Add(frame);
            return grid;
        }

        private UIElement MakeApplyButton()
        {
            var b = Ui.Btn("ACTIVER", "BtnOutline", 9);
            b.Padding = new Thickness(10, 6, 10, 6);
            b.Click += (s, e) => { ThemeEngine.ApplyDefault(); Close(); };
            return b;
        }

        private static Border Swatch(string hex)
        {
            return new Border
            {
                Width = 22,
                Height = 22,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
            };
        }

        private void Import()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Thème VintageDrive|*.vdtheme;*.json" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                ThemeEngine.Import(dialog.FileName);
                Close();
            }
            catch (Exception ex)
            {
                Dialogs.Info(this, "THÈME ILLISIBLE", ex.Message);
            }
        }

        private void Export()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Thème VintageDrive|*.vdtheme",
                FileName = ThemeEngine.CurrentName.ToLowerInvariant().Replace(' ', '-') + ".vdtheme",
            };
            if (dialog.ShowDialog() != true) return;
            ThemeEngine.Export(dialog.FileName);
            Dialogs.Info(this, "THÈME EXPORTÉ", "Ouvre le fichier, modifie les couleurs (format #RRGGBB), renomme-le, réimporte-le — et partage-le !");
        }
    }
}
