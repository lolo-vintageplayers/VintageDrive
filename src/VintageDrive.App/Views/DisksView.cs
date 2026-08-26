#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Util;

namespace VintageDrive.App.Views
{
    /// <summary>Écran principal : inventaire des supports + actions.</summary>
    public class DisksView : UserControl
    {
        private readonly MainWindow _main;
        private List<PhysicalDisk> _disks = new List<PhysicalDisk>();
        private PhysicalDisk _selected;

        private StackPanel _list;
        private StackPanel _actionsBox;

        public DisksView(MainWindow main)
        {
            _main = main;
            Content = Build();
            RefreshDisks();
        }

        private UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(28, 22, 28, 12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── En-tête : logo + titre + thème/version
            var logo = new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),
                Height = 78,
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);

            var titleBox = Ui.VStack(5,
                Ui.HardTitle("VINTAGEDRIVE", 26),
                Ui.P("// LE LABO DU STOCKAGE RÉTRO", 11, "GoldBrush"));
            titleBox.VerticalAlignment = VerticalAlignment.Center;

            var themeChip = new Button
            {
                Style = Ui.S("FootChip"),
                BorderBrush = Ui.B("PeriBrush"),
                Background = Ui.B("PanelBrush"),
                Padding = new Thickness(10, 4, 10, 4),
                Content = Ui.HStack(8,
                    Ui.Icon(Ui.GeoPalette, "GoldBrush", 15),
                    Ui.T("THÈME : " + ThemeEngine.CurrentName.ToUpperInvariant(), 16, "LavenderBrush")),
            };
            themeChip.Click += (s, e) =>
            {
                string before = ThemeEngine.CurrentName;
                Dialogs.ShowWithDim(Window.GetWindow(this), new ThemesWindow { Owner = Window.GetWindow(this) });
                if (ThemeEngine.CurrentName != before) _main.ShowDisks("THÈME : " + ThemeEngine.CurrentName);
            };

            var rightBox = Ui.VStack(6, themeChip, Ui.T(MainWindow.Version + " · libre & open source (MIT)", 15, "DimBrush"));
            rightBox.HorizontalAlignment = HorizontalAlignment.Right;

            var header = Ui.Spread(Ui.HStack(18, logo, titleBox), rightBox);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Ligne de section + actualiser
            var refresh = new Button
            {
                Style = Ui.S("FootChip"),
                BorderBrush = Ui.B("DimBorderBrush"),
                Padding = new Thickness(8, 1, 8, 1),
                Content = Ui.T("ACTUALISER", 15, "DimBrush"),
            };
            refresh.Click += (s, e) => RefreshDisks();
            var sectionRow = Ui.Spread(Ui.P("▶ SUPPORTS DÉTECTÉS", 13, "GoldBrush", bold: true), refresh);
            sectionRow.Margin = new Thickness(0, 16, 0, 12);
            Grid.SetRow(sectionRow, 1);
            root.Children.Add(sectionRow);

            // ── Contenu : liste + panneau d'actions
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

            _list = new StackPanel();
            var scroll = new ScrollViewer
            {
                Content = _list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 20, 0),
            };
            Grid.SetColumn(scroll, 0);
            content.Children.Add(scroll);

            _actionsBox = new StackPanel();
            Grid.SetColumn(_actionsBox, 1);
            content.Children.Add(_actionsBox);

            Grid.SetRow(content, 2);
            root.Children.Add(content);
            return root;
        }

        // ── Données ─────────────────────────────────────────────────────────
        public async void RefreshDisks()
        {
            try
            {
                var disks = await Task.Run(() => DiskEnumerator.GetDisks());
                _disks = disks.ToList();
                _selected = _selected != null ? _disks.FirstOrDefault(d => d.Index == _selected.Index && !d.IsSystemDisk) : null;
                if (_selected == null)
                    _selected = _disks.FirstOrDefault(d => !d.IsSystemDisk && IsRemovable(d));
                BuildCards();
                BuildActions();
            }
            catch (Exception ex)
            {
                Dialogs.Info(Window.GetWindow(this), "ERREUR", "Inventaire impossible : " + ex.Message);
            }
        }

        private static bool IsRemovable(PhysicalDisk d)
            => d.Bus == StorageBus.Usb || d.Bus == StorageBus.Sd || d.Bus == StorageBus.Mmc || d.IsRemovableMedia;

        private void Select(PhysicalDisk d)
        {
            if (d.IsSystemDisk) return;
            _selected = d;
            BuildCards();
            BuildActions();
        }

        // ── Cartes disques ──────────────────────────────────────────────────
        private void BuildCards()
        {
            _list.Children.Clear();
            if (_disks.Count == 0)
            {
                _list.Children.Add(Ui.Card(Ui.T("Aucun disque détecté. Branche un support et clique ACTUALISER.", 18, "DimBrush", wrap: true)));
                return;
            }
            foreach (var d in _disks)
            {
                var card = BuildCard(d);
                if (card is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 0, 14);
                _list.Children.Add(card);
            }
        }

        private UIElement BuildCard(PhysicalDisk d)
        {
            bool selected = _selected != null && _selected.Index == d.Index;
            bool removable = IsRemovable(d);
            string iconGeo = d.IsSystemDisk ? Ui.GeoLock : removable ? Ui.GeoUsb : Ui.GeoHdd;
            string iconBrush = d.IsSystemDisk ? "RedBrush" : removable ? (selected ? "GoldBrush" : "LavenderBrush") : "OrangeBrush";

            // ligne 1 : icône · DISQUE n · modèle · badge
            var line1 = new DockPanel { LastChildFill = false };
            var left1 = Ui.HStack(12,
                Ui.Icon(iconGeo, iconBrush),
                Ui.P((selected ? "▶ " : "") + "DISQUE " + d.Index, 10, selected ? "GoldBrush" : "LavenderBrush", bold: selected),
                Ui.T(string.IsNullOrEmpty(d.Model) ? "(modèle inconnu)" : d.Model, 20, "BrightBrush"));
            DockPanel.SetDock(left1, Dock.Left);
            line1.Children.Add(left1);

            Border badge = null;
            if (d.IsSystemDisk) badge = Ui.Badge("SYSTÈME — VERROUILLÉ", "RedBrush");
            else if (selected) badge = Ui.Badge("SÉLECTIONNÉ", "GoldBrush", filled: true);
            else if (!removable) badge = Ui.Badge("DISQUE INTERNE", "OrangeBrush");
            if (badge != null)
            {
                DockPanel.SetDock(badge, Dock.Right);
                line1.Children.Add(badge);
            }

            // ligne 2 : chips + volumes + taille
            var line2 = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 8, 0, 0) };
            var chips = new List<UIElement>();
            chips.Add(Ui.Chip(BusName(d.Bus), d.Bus == StorageBus.Usb || d.Bus == StorageBus.Sd ? "CyanBrush" : "LavenderBrush"));
            chips.Add(Ui.Chip(d.PartitionStyle == PartStyle.Mbr ? "MBR" : d.PartitionStyle == PartStyle.Gpt ? "GPT" : "RAW", "LavenderBrush"));
            if (d.IsRemovableMedia) chips.Add(Ui.Chip("amovible", "LavenderBrush"));
            string vol = VolumeSummary(d);
            chips.Add(Ui.T(vol, 17, d.IsSystemDisk || !removable ? "DimBrush" : "LavenderBrush"));
            var left2 = Ui.HStack(8, chips.ToArray());
            DockPanel.SetDock(left2, Dock.Left);
            line2.Children.Add(left2);

            var size = Ui.T(ByteFormatter.Decimal(d.SizeBytes), 22, selected ? "GoldBrush" : "BrightBrush");
            DockPanel.SetDock(size, Dock.Right);
            line2.Children.Add(size);

            var body = new StackPanel();
            body.Children.Add(line1);
            body.Children.Add(line2);

            var card = Ui.Card(body, selected ? "GoldBrush" : "DimBorderBrush");
            if (d.IsSystemDisk) card.Opacity = 0.55;
            else
            {
                card.Cursor = System.Windows.Input.Cursors.Hand;
                card.MouseLeftButtonUp += (s, e) => Select(d);
            }
            return card;
        }

        private static string VolumeSummary(PhysicalDisk d)
        {
            if (d.IsSystemDisk) return d.Volumes.Count > 0 ? $"{d.Volumes[0].Letter} · jamais proposé au formatage, quoi qu'il arrive" : "";
            if (d.Volumes.Count == 0) return "aucun volume monté";
            var v = d.Volumes[0];
            if (!v.IsReady) return $"{v.Letter} · RAW / non formaté";
            string label = string.IsNullOrEmpty(v.Label) ? "" : $" · « {v.Label} »";
            return $"{v.Letter} · {v.FileSystem}{label} · {ByteFormatter.Decimal(v.FreeBytes)} libres";
        }

        private static string BusName(StorageBus bus)
        {
            switch (bus)
            {
                case StorageBus.Usb: return "USB";
                case StorageBus.Sata: return "SATA";
                case StorageBus.Nvme: return "NVMe";
                case StorageBus.Sd: return "SD";
                case StorageBus.Mmc: return "MMC";
                default: return bus.ToString().ToUpperInvariant();
            }
        }

        // ── Panneau d'actions ───────────────────────────────────────────────
        private void BuildActions()
        {
            _actionsBox.Children.Clear();

            var inner = new StackPanel();
            if (_selected == null)
            {
                inner.Children.Add(Ui.P("AUCUN SUPPORT SÉLECTIONNÉ", 11, "LavenderBrush", bold: true));
                inner.Children.Add(Ui.T("Branche une clé USB, une carte SD ou un disque, puis clique dessus.", 17, "DimBrush", wrap: true));
                var emptyCard = Ui.Card(inner, "DimBorderBrush");
                emptyCard.Margin = new Thickness(0, 0, 0, 14);
                _actionsBox.Children.Add(emptyCard);
            }
            else
            {
                var d = _selected;
                string title = d.Volumes.Count > 0
                    ? d.Volumes[0].Letter + (d.Volumes[0].IsReady && !string.IsNullOrEmpty(d.Volumes[0].Label) ? $" — « {d.Volumes[0].Label} »" : " —")
                    : "DISQUE " + d.Index;
                inner.Children.Add(Ui.P(title, 12, "GoldBrush", bold: true));
                var sub = Ui.T($"{d.Model} · {BusName(d.Bus)} · {ByteFormatter.Decimal(d.SizeBytes)}", 17, "LavenderBrush", wrap: true);
                sub.Margin = new Thickness(0, 8, 0, 14);
                inner.Children.Add(sub);

                var btnTest = Ui.Btn("▶ TESTER LA CAPACITÉ", "BtnGold");
                btnTest.Click += (s, e) => StartTest(d);
                btnTest.Margin = new Thickness(0, 0, 0, 12);

                var btnFormat = Ui.Btn("▶ FORMATER", "BtnOutline");
                btnFormat.Click += (s, e) => StartFormat(d);
                btnFormat.Margin = new Thickness(0, 0, 0, 12);

                var btnInfo = Ui.Btn("▶ INFORMATIONS", "BtnOutline");
                btnInfo.BorderBrush = Ui.B("CyanBrush");
                btnInfo.Foreground = Ui.B("CyanBrush");
                btnInfo.Click += (s, e) => _main.ShowInfo(d); // lecture seule : ni admin, ni garde-fou
                btnInfo.Margin = new Thickness(0, 0, 0, 12);

                var btnWipe = Ui.Btn("▶ EFFACER", "BtnDanger");
                btnWipe.Click += (s, e) => StartWipe(d);

                inner.Children.Add(btnTest);
                inner.Children.Add(btnFormat);
                inner.Children.Add(btnInfo);
                inner.Children.Add(btnWipe);

                var actionCard = Ui.Card(inner, "PeriBrush");
                actionCard.Margin = new Thickness(0, 0, 0, 14);
                _actionsBox.Children.Add(actionCard);
            }

            var hint = Ui.HStack(10,
                Ui.Icon(Ui.GeoInfo, "CyanBrush", 18),
                Ui.T("Acheté pas cher ? Teste AVANT d'y copier tes jeux : 2 minutes suffisent pour démasquer une fausse capacité.", 17, "LavenderBrush", wrap: true));
            if (hint.Children[1] is FrameworkElement txt) txt.MaxWidth = 300;
            _actionsBox.Children.Add(Ui.Card(hint, "DimBorderBrush", "PanelDarkBrush"));
        }

        // ── Actions ────────────────────────────────────────────────────────
        private bool GuardInternal(PhysicalDisk d, string action)
        {
            if (IsRemovable(d)) return true;
            return Dialogs.Confirm(Window.GetWindow(this), "DISQUE INTERNE",
                $"« {d.Model} » est un DISQUE INTERNE de ce PC, pas une clé USB.\n" +
                $"Es-tu absolument sûr de vouloir {action} CE disque ?",
                "OUI, C'EST BIEN CELUI-LÀ", danger: true);
        }

        private void StartTest(PhysicalDisk d)
        {
            if (!_main.EnsureAdmin()) return;
            if (!GuardInternal(d, "tester (destructif)")) return;

            var kind = Dialogs.TestChoice(Window.GetWindow(this), d);
            if (kind == TestKind.Cancel) return;

            if (kind == TestKind.Quick)
            {
                var pv = new ProgressView(_main, "▶ TEST RAPIDE DE CAPACITÉ",
                    $"{Letter(d)}{d.Model} · {ByteFormatter.Decimal(d.SizeBytes)} annoncés · durée estimée affichée après calibrage", showBar: true);
                _main.ShowProgress(pv);
                pv.StartProbe(d);
            }
            else
            {
                var pv = new ProgressView(_main, "▶ TEST COMPLET — 100 % DE LA SURFACE",
                    $"{Letter(d)}{d.Model} · {ByteFormatter.Decimal(d.SizeBytes)} annoncés · écrit puis relit chaque octet", showBar: true);
                _main.ShowProgress(pv);
                pv.StartFullTest(d);
            }
        }

        private void StartFormat(PhysicalDisk d)
        {
            if (!_main.EnsureAdmin()) return;
            if (!GuardInternal(d, "formater")) return;
            _main.ShowFormat(d);
        }

        private void StartWipe(PhysicalDisk d)
        {
            if (!_main.EnsureAdmin()) return;
            if (!GuardInternal(d, "effacer")) return;
            var choice = Dialogs.WipeChoice(Window.GetWindow(this), d);
            if (choice == WipeKind.Cancel) return;

            string title = choice == WipeKind.Quick ? "▶ NETTOYAGE RAPIDE" : "▶ EFFACEMENT COMPLET";
            var pv = new ProgressView(_main, title, $"{Letter(d)}{d.Model} · {ByteFormatter.Decimal(d.SizeBytes)}", showBar: false);
            _main.ShowProgress(pv);
            pv.StartWork(
                (log, ct) =>
                {
                    var r = choice == WipeKind.Quick
                        ? Core.Wipe.Wiper.QuickClean(d, log, ct)
                        : Core.Wipe.Wiper.FullWipe(d, log, ct);
                    string extra = r.WriteErrors > 0 ? $"\n{r.WriteErrors} erreurs d'écriture : support probablement en fin de vie." : "";
                    return $"{ByteFormatter.Decimal(r.BytesWritten)} mis à zéro en {r.Duration.TotalSeconds:F0} s.{extra}\nLe support est vierge (RAW) : formate-le avant usage.";
                },
                "EFFACEMENT TERMINÉ");
        }

        private static string Letter(PhysicalDisk d)
            => d.Volumes.Count > 0 ? d.Volumes[0].Letter + " — " : "";
    }
}
