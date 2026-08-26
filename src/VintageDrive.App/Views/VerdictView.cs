#nullable disable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VintageDrive.Core.Capacity;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Util;

namespace VintageDrive.App.Views
{
    /// <summary>Écran de verdict : GAME OVER (falsifié) ou STAGE CLEAR (conforme).</summary>
    public class VerdictView : UserControl
    {
        private readonly MainWindow _main;
        private readonly ProbeResult _r;
        private readonly PhysicalDisk _disk;
        private readonly bool _fullSurface;
        private FrameworkElement _captureRoot;
        private DispatcherTimer _blinkTimer;

        public VerdictView(MainWindow main, ProbeResult r, PhysicalDisk disk, bool fullSurface = false)
        {
            _main = main;
            _r = r;
            _disk = disk;
            _fullSurface = fullSurface;
            Content = Build();
        }

        private UIElement Build()
        {
            bool ok = _r.Verdict == CapacityVerdict.Conforme;
            bool dying = _r.Verdict == CapacityVerdict.Defaillant || _r.Verdict == CapacityVerdict.Incoherent;

            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 980 };

            var head = Ui.T($"RÉSULTAT DU TEST — {_disk.Model} · {ByteFormatter.Decimal(_r.ClaimedBytes)} annoncés", 19, "LavenderBrush");
            head.HorizontalAlignment = HorizontalAlignment.Center;
            head.Margin = new Thickness(0, 0, 0, 14);
            column.Children.Add(head);

            string bigText = ok ? "STAGE CLEAR !" : dying ? "SUPPORT KO" : "GAME OVER";
            string bigBrush = ok ? "GreenBrush" : dying ? "OrangeBrush" : "RedBrush";
            var big = Ui.HardTitle(bigText, ok ? 48 : 54, bigBrush, offset: 5);
            big.HorizontalAlignment = HorizontalAlignment.Center;
            column.Children.Add(big);

            string subText = ok ? "CAPACITÉ CONFORME"
                : _r.Verdict == CapacityVerdict.Defaillant ? "TROP D'ERREURS D'ENTRÉE/SORTIE"
                : _r.Verdict == CapacityVerdict.Incoherent ? "RÉSULTATS INSTABLES"
                : "CAPACITÉ FALSIFIÉE";
            var sub = Ui.P(subText, ok ? 14 : 15, ok ? "GoldBrush" : dying ? "OrangeBrush" : "MagentaBrush", bold: true);
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            sub.Margin = new Thickness(0, 10, 0, 18);
            column.Children.Add(sub);

            column.Children.Add(ok ? BuildOkCard() : BuildFakeCard(dying));

            if (!ok && !dying)
            {
                var blink = Ui.P("INSERT REAL DRIVE TO CONTINUE", 12, "CyanBrush");
                blink.HorizontalAlignment = HorizontalAlignment.Center;
                blink.Margin = new Thickness(0, 16, 0, 0);
                column.Children.Add(blink);
                _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _blinkTimer.Tick += (s, e) => blink.Opacity = blink.Opacity > 0.5 ? 0 : 1;
                _blinkTimer.Start();
            }

            var credit = Ui.T(
                $"VINTAGEDRIVE · test du {DateTime.Now:dd/MM/yyyy} · {(_fullSurface ? "100 % de la surface vérifiée" : _r.PointsTotal + " points vérifiés")} · youtube.com/@vintageplayerss",
                15, "DimBrush");
            credit.HorizontalAlignment = HorizontalAlignment.Center;
            credit.Margin = new Thickness(0, 16, 0, 0);
            column.Children.Add(credit);

            _captureRoot = new Border
            {
                Background = Ui.B("BgBrush"),
                Padding = new Thickness(36, 28, 36, 24),
                Child = column,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            // ── Boutons hors capture
            var back = Ui.Btn("◀ RETOUR", "BtnOutline", 11);
            back.Click += (s, e) => { _blinkTimer?.Stop(); _main.ShowDisks(); };

            var savePng = Ui.Btn(ok ? "ENREGISTRER LE RAPPORT (PNG)" : "ENREGISTRER LA PREUVE (PNG)", "BtnGold", 11);
            savePng.Margin = new Thickness(12, 0, 0, 0);
            savePng.Click += (s, e) => SavePng();

            var reformat = Ui.Btn(ok ? "▶ FORMATER CE SUPPORT" : "REFORMATER À LA VRAIE TAILLE", "BtnOutline", 11);
            reformat.Margin = new Thickness(12, 0, 0, 0);
            reformat.Click += (s, e) => { _blinkTimer?.Stop(); _main.ShowFormat(_disk); };

            var buttons = Ui.HStack(0, back, savePng, reformat);
            buttons.HorizontalAlignment = HorizontalAlignment.Center;
            buttons.Margin = new Thickness(0, 14, 0, 10);

            var root = new StackPanel { Margin = new Thickness(24, 18, 24, 6) };
            root.Children.Add(_captureRoot);
            root.Children.Add(buttons);
            return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement BuildFakeCard(bool dying)
        {
            var panel = new StackPanel();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var claimed = Ui.VStack(6,
                Center(Ui.P("ANNONCÉ", 11, "PeriBrush", bold: true)),
                Center(Ui.HStack(12, Ui.Icon(Ui.GeoCross, "RedBrush", 26, 3), Ui.T(ByteFormatter.Decimal(_r.ClaimedBytes), 44, "DimBrush"))));
            Grid.SetColumn(claimed, 0);
            grid.Children.Add(claimed);

            var real = Ui.VStack(6,
                Center(Ui.P("RÉEL", 11, "GoldBrush", bold: true)),
                Center(Ui.HStack(12, Ui.Icon(Ui.GeoCheck, "GreenBrush", 26, 3), Ui.T(ByteFormatter.Decimal(_r.EstimatedRealBytes), 44, "GoldBrush"))));
            Grid.SetColumn(real, 1);
            grid.Children.Add(real);
            panel.Children.Add(grid);

            double frac = _r.ClaimedBytes > 0 ? Math.Min(1.0, (double)_r.EstimatedRealBytes / _r.ClaimedBytes) : 0;
            var fill = new Border
            {
                Background = Ui.B("GoldBrush"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(4, 860 * frac),
            };
            var track = new Border
            {
                BorderBrush = Ui.B("PeriBrush"),
                BorderThickness = new Thickness(2),
                Background = Ui.B("PanelDarkBrush"),
                Padding = new Thickness(4),
                Height = 30,
                Width = 872,
                Child = fill,
            };
            var barRow = Ui.HStack(14, track, Ui.T($"{frac * 100:F0} % de la promesse", 18, "LavenderBrush"));
            barRow.Margin = new Thickness(0, 16, 0, 14);
            panel.Children.Add(barRow);

            string typeText = _fullSurface
                ? "Au-delà de la capacité réelle, les données relues ne correspondent plus à ce qui a été écrit."
                : _r.Verdict == CapacityVerdict.FakeWrap
                ? "Contrôleur qui boucle : les écritures hautes écrasent les données basses."
                : _r.Verdict == CapacityVerdict.FakeDiscard
                ? "Les écritures au-delà de la puce réelle sont silencieusement jetées."
                : _r.Verdict == CapacityVerdict.Defaillant
                ? $"{_r.PointsIoError} erreurs d'entrée/sortie sur {_r.PointsTotal} points : ce support est en train de mourir."
                : "Résultats non reproductibles : support instable, à ne pas utiliser pour des données importantes.";
            panel.Children.Add(Ui.T(typeText, 19, "LavenderBrush", wrap: true));

            if (!dying)
            {
                var warn = Ui.T($"→ Tout fichier copié au-delà de {ByteFormatter.Decimal(_r.EstimatedRealBytes)} serait silencieusement corrompu.", 20, "RedBrush", wrap: true);
                warn.Margin = new Thickness(0, 4, 0, 0);
                panel.Children.Add(warn);
            }

            if (!_fullSurface)
            {
                var detail = Ui.T($"{_r.PointsOk} points intacts · {_r.PointsForeign} écrasés · {_r.PointsGarbage} perdus · {_r.PointsIoError} erreurs E/S · durée {_r.Duration.TotalSeconds:F0} s", 15, "DimBrush");
                detail.Margin = new Thickness(0, 10, 0, 0);
                panel.Children.Add(detail);
            }

            return Ui.Card(panel, dying ? "OrangeBrush" : "RedBrush", "PanelBrush", new Thickness(28, 22, 28, 18));
        }

        private UIElement BuildOkCard()
        {
            var panel = new StackPanel();
            panel.Children.Add(Center(Ui.T($"{ByteFormatter.Decimal(_r.ClaimedBytes)} annoncés — {ByteFormatter.Decimal(_r.EstimatedRealBytes)} vérifiés", 32, "BrightBrush")));
            var sub = Center(Ui.T(_fullSurface
                ? "Écriture puis relecture de 100 % de la surface — preuve définitive, octet par octet"
                : $"{_r.PointsTotal} blocs signés relus intacts, répartis sur 100 % de la plage adressable", 18, "LavenderBrush"));
            sub.Margin = new Thickness(0, 4, 0, _fullSurface ? 14 : 4);
            panel.Children.Add(sub);

            if (!_fullSurface)
            {
                var hint = Center(Ui.T("Test échantillonné : toute capacité sérieusement gonflée serait tombée. Pour la preuve octet par octet, lance le TEST COMPLET.", 15, "DimBrush"));
                hint.Margin = new Thickness(0, 0, 0, 14);
                panel.Children.Add(hint);
            }

            var sep = new Border { BorderBrush = Ui.B("DimBorderBrush"), BorderThickness = new Thickness(0, 2, 0, 0), Padding = new Thickness(0, 14, 0, 0) };
            var stats = new Grid();
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            AddStat(stats, 0, _r.SeqReadMBps > 0 ? $"{_r.SeqReadMBps:F1} Mo/s" : "—", "LECTURE SÉQ.", "BrightBrush");
            AddStat(stats, 1, _r.SeqWriteMBps > 0 ? $"{_r.SeqWriteMBps:F1} Mo/s" : "—", "ÉCRITURE SÉQ.", "BrightBrush");
            AddStat(stats, 2, _r.SeqReadMBps > 60 ? "USB 3.0" : $"{_r.Duration.TotalSeconds:F0} s", _r.SeqReadMBps > 60 ? "CONFIRMÉ EN LECTURE" : "DURÉE DU TEST", "CyanBrush");
            sep.Child = stats;
            panel.Children.Add(sep);

            return Ui.Card(panel, "GreenBrush", "PanelBrush", new Thickness(28, 22, 28, 18));
        }

        private static void AddStat(Grid grid, int col, string value, string label, string valueBrush)
        {
            var box = Ui.VStack(6, Center(Ui.T(value, 27, valueBrush)), Center(Ui.P(label, 10, "PeriBrush")));
            Grid.SetColumn(box, col);
            grid.Children.Add(box);
        }

        private static FrameworkElement Center(FrameworkElement e)
        {
            e.HorizontalAlignment = HorizontalAlignment.Center;
            return e;
        }

        private void SavePng()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Image PNG|*.png",
                FileName = $"vintagedrive-{(_r.Verdict == CapacityVerdict.Conforme ? "rapport" : "preuve")}-{DateTime.Now:yyyyMMdd-HHmm}.png",
            };
            if (dialog.ShowDialog() != true) return;

            var el = _captureRoot;
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(el.ActualWidth), (int)Math.Ceiling(el.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(el);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = System.IO.File.Create(dialog.FileName))
                encoder.Save(fs);
            _main.UpdateFooter("PREUVE ENREGISTRÉE : " + System.IO.Path.GetFileName(dialog.FileName));
        }
    }
}
