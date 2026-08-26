#nullable disable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VintageDrive.App.Views
{
    /// <summary>Fenêtre « ? AIDE » : version, liens, premiers pas.</summary>
    public class HelpWindow : Window
    {
        public HelpWindow()
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
            var body = new StackPanel { Width = 620 };

            body.Children.Add(Ui.HardTitle("VINTAGEDRIVE", 24));
            var version = Ui.T($"{MainWindow.Version} · libre & open source (licence MIT) · par Vintage Players", 19, "LavenderBrush");
            version.Margin = new Thickness(0, 8, 0, 18);
            body.Children.Add(version);

            body.Children.Add(Ui.P("▶ PREMIERS PAS", 12, "GoldBrush", bold: true));
            var steps = Ui.T(
                "1. Branche ta clé USB, ta carte SD ou ton disque.\n" +
                "2. TESTER LA CAPACITÉ — 2 minutes pour démasquer une fausse capacité (efface tout).\n" +
                "3. FORMATER — choisis ta console (Wii, PS2, GDEMU…), les bons réglages sont appliqués tout seuls.\n" +
                "Le disque système de ton PC est verrouillé : aucun risque de le toucher.",
                18, "LavenderBrush", wrap: true);
            steps.Margin = new Thickness(0, 8, 0, 18);
            body.Children.Add(steps);

            body.Children.Add(Ui.P("▶ BESOIN D'AIDE ? UNE FAKE À SIGNALER ?", 12, "GoldBrush", bold: true));
            var yt = LinkButton("YOUTUBE — les tutos Vintage Players", "RedBrush", "https://www.youtube.com/@vintageplayerss?sub_confirmation=1");
            yt.Margin = new Thickness(0, 10, 0, 8);
            var dc = LinkButton("DISCORD — la communauté répond", "PeriBrush", "https://discord.gg/d68NjkPRMz");
            dc.Margin = new Thickness(0, 0, 0, 8);
            var site = LinkButton("VINTAGEPLAYERS.FR — Tout le retrogaming", "CyanBrush", "https://vintageplayers.fr/");
            site.Margin = new Thickness(0, 0, 0, 18);
            body.Children.Add(yt);
            body.Children.Add(dc);
            body.Children.Add(site);

            if (!MainWindow.IsElevated())
            {
                var adminNote = Ui.T("Mode sans droits admin : l'inventaire fonctionne, mais tester/formater/effacer demanderont une relance en administrateur.", 17, "OrangeBrush", wrap: true);
                adminNote.Margin = new Thickness(0, 0, 0, 14);
                body.Children.Add(adminNote);
            }

            var credits = Ui.T("Polices Silkscreen & VT323 (licence SIL OFL) embarquées. Formateur FAT32 : implémentation originale d'après la spécification Microsoft.", 15, "DimBrush", wrap: true);
            credits.Margin = new Thickness(0, 0, 0, 14);
            body.Children.Add(credits);

            var close = Ui.Btn("FERMER", "BtnGold", 11);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += (s, e) => Close();
            body.Children.Add(close);

            var frame = new Border
            {
                Background = Ui.B("PanelBrush"),
                BorderBrush = Ui.B("PeriBrush"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(26, 20, 26, 20),
                Margin = new Thickness(0, 0, 5, 5),
                Child = body,
            };
            var shadow = new Border { Background = Brushes.Black, Margin = new Thickness(5, 5, 0, 0) };
            var grid = new Grid();
            grid.Children.Add(shadow);
            grid.Children.Add(frame);
            return grid;
        }

        private static Button LinkButton(string text, string brushKey, string url)
        {
            var b = new Button
            {
                Style = Ui.S("FootChip"),
                BorderBrush = Ui.B(brushKey),
                Padding = new Thickness(12, 6, 12, 7),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = Ui.T(text, 19, brushKey),
            };
            b.Click += (s, e) => MainWindow.OpenUrl(url);
            return b;
        }
    }
}
