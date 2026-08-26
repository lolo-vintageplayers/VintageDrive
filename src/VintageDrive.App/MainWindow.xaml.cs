#nullable disable
using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using VintageDrive.App.Views;
using VintageDrive.Core.Capacity;
using VintageDrive.Core.Disks;

namespace VintageDrive.App
{
    public partial class MainWindow : Window
    {
        public const string Version = "v1.0";
        private DispatcherTimer _deviceDebounce;

        public MainWindow()
        {
            InitializeComponent();
            Screen.Content = new DisksView(this);
            UpdateFooter("PRÊT");
            SourceInitialized += OnSourceInitialized;
        }

        // ── Navigation entre écrans ─────────────────────────────────────────
        public void ShowDisks(string statusMessage = null)
        {
            var view = new DisksView(this);
            Screen.Content = view;
            UpdateFooter(statusMessage ?? "PRÊT");
        }

        public void ShowFormat(PhysicalDisk disk)
        {
            Screen.Content = new FormatView(this, disk);
            UpdateFooter("FORMATAGE — choisis ta console");
        }

        public void ShowProgress(ProgressView view)
        {
            Screen.Content = view;
            UpdateFooter("OPÉRATION EN COURS…");
        }

        public void ShowVerdict(ProbeResult result, PhysicalDisk disk)
        {
            Screen.Content = new VerdictView(this, result, disk);
            UpdateFooter(result.Verdict == CapacityVerdict.Conforme ? "CAPACITÉ CONFORME" : "CAPACITÉ NON CONFORME");
        }

        public void ShowInfo(PhysicalDisk disk)
        {
            Screen.Content = new InfoView(this, disk);
            UpdateFooter("INFORMATIONS DU SUPPORT");
        }

        public void ShowVerdictFull(FullTestResult result, PhysicalDisk disk)
        {
            var mapped = ProbeResult.FromFullSurface(result);
            Screen.Content = new VerdictView(this, mapped, disk, fullSurface: true);
            UpdateFooter(result.Conforme ? "CAPACITÉ CONFORME (100 % VÉRIFIÉ)" : "CAPACITÉ NON CONFORME");
        }

        public void UpdateFooter(string status)
        {
            bool admin = IsElevated();
            FootStatus.Text = $"{status} · ADMIN : {(admin ? "OUI" : "NON")} · {Version} · thème Vintage Players";
            FootStatus.Foreground = admin
                ? (System.Windows.Media.Brush)FindResource("DimBrush")
                : (System.Windows.Media.Brush)FindResource("OrangeBrush");
        }

        // ── Droits admin ────────────────────────────────────────────────────
        public static bool IsElevated()
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        /// <summary>Vrai si on peut agir ; sinon propose la relance en administrateur.</summary>
        public bool EnsureAdmin()
        {
            if (IsElevated()) return true;
            bool relaunch = Dialogs.Confirm(this,
                "DROITS ADMINISTRATEUR REQUIS",
                "Écrire directement sur un disque demande les droits administrateur.\n" +
                "Relancer VintageDrive en administrateur ? (tes réglages seront conservés)",
                "RELANCER EN ADMIN", danger: false);
            if (relaunch)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = Process.GetCurrentProcess().MainModule.FileName,
                        UseShellExecute = true,
                        Verb = "runas",
                    };
                    Process.Start(psi);
                    Application.Current.Shutdown();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // UAC refusée : on reste là, sans drame
                }
            }
            return false;
        }

        // ── Détection branchement/débranchement USB (WM_DEVICECHANGE) ──────
        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            source?.AddHook(WndProc);
            _deviceDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _deviceDebounce.Tick += (s, args) =>
            {
                _deviceDebounce.Stop();
                (Screen.Content as DisksView)?.RefreshDisks();
            };
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DEVICECHANGE = 0x0219;
            if (msg == WM_DEVICECHANGE && Screen.Content is DisksView)
            {
                _deviceDebounce.Stop();
                _deviceDebounce.Start();
            }
            return IntPtr.Zero;
        }

        /// <summary>Assombrit et floute le fond pendant qu'une fenêtre modale est ouverte.</summary>
        public void Dim(bool on)
        {
            DimOverlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            RootContent.Effect = on ? new System.Windows.Media.Effects.BlurEffect { Radius = 6 } : null;
        }

        // ── Pied de page ────────────────────────────────────────────────────
        private void OnAide(object sender, RoutedEventArgs e)
        {
            Views.Dialogs.ShowWithDim(this, new HelpWindow { Owner = this });
        }

        private void OnLink(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string url)
                OpenUrl(url);
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* pas de navigateur ? tant pis */ }
        }
    }
}
