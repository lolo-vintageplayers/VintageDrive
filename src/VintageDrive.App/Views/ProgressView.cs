#nullable disable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VintageDrive.Core.Capacity;
using VintageDrive.Core.Disks;

namespace VintageDrive.App.Views
{
    /// <summary>Écran de progression générique : test de capacité, formatage, effacement.</summary>
    public class ProgressView : UserControl
    {
        private readonly MainWindow _main;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private TextBlock _phase;
        private TextBlock _counter;
        private TextBlock _wait;
        private System.Windows.Threading.DispatcherTimer _blink;
        private TextBlock _percent;
        private Border _barTrack;
        private Border _barFill;
        private StackPanel _logPanel;
        private ScrollViewer _logScroll;
        private Button _cancel;

        public ProgressView(MainWindow main, string title, string subtitle, bool showBar)
        {
            _main = main;
            Content = Build(title, subtitle, showBar);
        }

        private UIElement Build(string title, string subtitle, bool showBar)
        {
            var root = new StackPanel { Margin = new Thickness(60, 36, 60, 12) };

            root.Children.Add(Ui.P(title, 17, "GoldBrush", bold: true));
            var sub = Ui.T(subtitle, 19, "LavenderBrush");
            sub.Margin = new Thickness(0, 8, 0, 20);
            root.Children.Add(sub);

            // ── Panneau de progression
            var panel = new StackPanel();
            _phase = Ui.P("PRÉPARATION…", 12, "BrightBrush", bold: true);
            panel.Children.Add(_phase);

            if (showBar)
            {
                _barFill = new Border
                {
                    Background = SegmentBrush(),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0,
                };
                _barTrack = new Border
                {
                    BorderBrush = Ui.B("PeriBrush"),
                    BorderThickness = new Thickness(2),
                    Background = Ui.B("PanelDarkBrush"),
                    Padding = new Thickness(4),
                    Height = 42,
                    Child = _barFill,
                };
                _percent = Ui.T("0 %", 40, "GoldBrush");
                _percent.MinWidth = 110;
                _percent.TextAlignment = TextAlignment.Right;

                var barRow = new DockPanel { Margin = new Thickness(0, 14, 0, 14), LastChildFill = true };
                DockPanel.SetDock(_percent, Dock.Right);
                _percent.Margin = new Thickness(16, 0, 0, 0);
                barRow.Children.Add(_percent);
                barRow.Children.Add(_barTrack);
                panel.Children.Add(barRow);
            }

            _counter = Ui.T("", 22, "BrightBrush");
            panel.Children.Add(_counter);

            // clignotant « ça travaille » pour les phases sans compteur (calibrage…) :
            // sans lui, une minute sans mouvement ressemble à un plantage
            _wait = Ui.T("PATIENTE, ÇA TRAVAILLE…", 21, "GreenBrush");
            panel.Children.Add(_wait);
            _blink = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
            _blink.Tick += (s, e) => _wait.Opacity = _wait.Opacity > 0.5 ? 0.15 : 1.0;
            _blink.Start();

            var card = Ui.Card(panel, "PeriBrush");
            card.Margin = new Thickness(0, 0, 0, 18);
            root.Children.Add(card);

            // ── Console
            _logPanel = new StackPanel();
            _logScroll = new ScrollViewer
            {
                Content = _logPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Height = 230,
            };
            var logCard = Ui.Card(_logScroll, "DimBorderBrush", "PanelDarkBrush");
            logCard.Margin = new Thickness(0, 0, 0, 18);
            root.Children.Add(logCard);

            // ── Annuler
            _cancel = Ui.Btn("ANNULER", "BtnDanger", 12);
            _cancel.HorizontalAlignment = HorizontalAlignment.Right;
            _cancel.Click += (s, e) =>
            {
                _cancel.IsEnabled = false;
                Log("» annulation demandée…");
                _cts.Cancel();
            };
            root.Children.Add(_cancel);

            return root;
        }

        private static Brush SegmentBrush()
        {
            // blocs or de 16 px séparés de 4 px, façon barre de vie d'arcade
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                ((SolidColorBrush)Ui.B("GoldBrush")), null, new RectangleGeometry(new Rect(0, 0, 16, 40))));
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 20, 40),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
            };
            return brush;
        }

        // ── Remontées (thread moteur → UI) ─────────────────────────────────
        private void Post(Action a) => Dispatcher.BeginInvoke(a);

        public void Log(string line)
        {
            var t = Ui.T("» " + line.Trim(), 20, "LavenderBrush", wrap: true);
            t.Margin = new Thickness(0, 0, 0, 4);
            _logPanel.Children.Add(t);
            if (_logPanel.Children.Count > 250) _logPanel.Children.RemoveAt(0);
            _logScroll.ScrollToEnd();
        }

        private void OnProgress(ProbeProgress p)
        {
            _phase.Text = p.Phase.ToUpperInvariant();
            if (p.Total <= 0)
            {
                _counter.Text = "";
                if (_percent != null) _percent.Text = "";
                ShowWait(true);
                return; // phase sans compteur (calibrage, mesure de vitesse) : rien à chiffrer
            }
            ShowWait(false);
            if (p.Total > 0 && _barTrack != null)
            {
                double frac = Math.Min(1.0, (double)p.Done / p.Total);
                double avail = Math.Max(0, _barTrack.ActualWidth - 12);
                _barFill.Width = avail * frac;
                _percent.Text = $"{frac * 100:F0} %";
                _counter.Text = p.Total > 8192
                    ? $"{p.Done >> 10} / {p.Total >> 10} Gio"
                    : $"{p.Done} / {p.Total} blocs";
            }
        }

        private void ShowWait(bool on)
        {
            _wait.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on && !_blink.IsEnabled) _blink.Start();
            if (!on && _blink.IsEnabled) _blink.Stop();
        }

        // ── Lancements ─────────────────────────────────────────────────────
        public async void StartProbe(PhysicalDisk disk)
        {
            try
            {
                var result = await Task.Run(() => CapacityProbe.Run(
                    disk,
                    s => Post(() => Log(s)),
                    _cts.Token,
                    p => Post(() => OnProgress(p))));
                _main.ShowVerdict(result, disk);
            }
            catch (OperationCanceledException)
            {
                _main.ShowDisks("TEST ANNULÉ — le support n'a plus de partition, reformate-le");
            }
            catch (Exception ex)
            {
                Dialogs.Info(Window.GetWindow(this), "ERREUR DU TEST", ex.Message);
                _main.ShowDisks("PRÊT");
            }
        }

        public async void StartFullTest(PhysicalDisk disk)
        {
            try
            {
                var result = await Task.Run(() => FullSurfaceTest.Run(
                    disk,
                    s => Post(() => Log(s)),
                    _cts.Token,
                    p => Post(() => OnProgress(p))));
                _main.ShowVerdictFull(result, disk);
            }
            catch (OperationCanceledException)
            {
                _main.ShowDisks("TEST ANNULÉ — le support n'a plus de partition, reformate-le");
            }
            catch (Exception ex)
            {
                Dialogs.Info(Window.GetWindow(this), "ERREUR DU TEST", ex.Message);
                _main.ShowDisks("PRÊT");
            }
        }

        public async void StartWork(Func<Action<string>, CancellationToken, string> work, string doneTitle)
        {
            _phase.Text = "OPÉRATION EN COURS…";
            try
            {
                string summary = await Task.Run(() => work(s => Post(() => Log(s)), _cts.Token));
                Dialogs.Info(Window.GetWindow(this), doneTitle + " ✔", summary);
                _main.ShowDisks(doneTitle);
            }
            catch (OperationCanceledException)
            {
                _main.ShowDisks("OPÉRATION ANNULÉE");
            }
            catch (Exception ex)
            {
                Dialogs.Info(Window.GetWindow(this), "ERREUR", ex.Message);
                _main.ShowDisks("PRÊT");
            }
        }
    }
}
