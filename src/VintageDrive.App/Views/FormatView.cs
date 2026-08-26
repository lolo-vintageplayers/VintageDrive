#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Format;
using VintageDrive.Core.Presets;
using VintageDrive.Core.Util;

namespace VintageDrive.App.Views
{
    /// <summary>Écran de formatage : deux modes exclusifs — presets consoles OU réglages libres.</summary>
    public class FormatView : UserControl
    {
        private readonly MainWindow _main;
        private readonly PhysicalDisk _disk;

        private ConsolePreset _preset;      // null = mode libre
        private ConsolePreset _lastPreset;  // dernier preset, pour revenir du mode libre
        private bool _updating;
        private bool _zeroFirst;            // effacement complet (zéros 100 %) avant formatage

        private readonly Dictionary<string, Button> _chips = new Dictionary<string, Button>();
        private StackPanel _sideBox;
        private StackPanel _presetsBox;
        private StackPanel _libreBox;
        private Border _presetRadio;
        private Border _libreRadio;
        private TextBlock _presetTitle;
        private TextBlock _libreTitle;
        private ComboBox _fsBox;
        private ComboBox _clusterBox;
        private TextBox _nameBox;

        public FormatView(MainWindow main, PhysicalDisk disk)
        {
            _main = main;
            _disk = disk;
            _preset = _lastPreset = ConsolePresets.Find("wii");
            Content = Build();
            SyncManualControls();
            RestyleChips();
            RebuildSide();
            UpdateModeVisuals();
        }

        private string DiskTitle()
        {
            var v = _disk.Volumes.Count > 0 ? _disk.Volumes[0] : null;
            string letter = v != null ? v.Letter + " " : "";
            string label = v != null && v.IsReady && !string.IsNullOrEmpty(v.Label) ? $"« {v.Label} » · " : "";
            return $"FORMATER — {letter}{label}{ByteFormatter.Decimal(_disk.SizeBytes)}";
        }

        private UIElement Build()
        {
            var root = new Grid { Margin = new Thickness(28, 22, 28, 12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // ── En-tête
            var back = Ui.Btn("◀", "BtnOutline", 13);
            back.Padding = new Thickness(12, 8, 12, 8);
            back.Click += (s, e) => _main.ShowDisks();
            var header = Ui.Spread(
                Ui.HStack(14, back, Ui.P(DiskTitle(), 15, "BrightBrush", bold: true)),
                Ui.T($"{_disk.Model} · {_disk.Bus.ToString().ToUpperInvariant()}", 16, "DimBrush"));
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Corps
            var body = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });

            var left = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };

            // ── MODE 1 : presets consoles
            _presetRadio = MakeRadio();
            _presetTitle = Ui.P("CHOISIS TA CONSOLE", 13, "GoldBrush", bold: true);
            var presetHeader = MakeModeHeader(_presetRadio, _presetTitle);
            presetHeader.MouseLeftButtonUp += (s, e) => SwitchToPresetMode();
            left.Children.Add(presetHeader);

            _presetsBox = new StackPanel();
            foreach (var group in ConsolePresets.All.GroupBy(p => p.Category))
            {
                var cat = Ui.P(group.Key.ToUpperInvariant(), 10, "PeriBrush");
                cat.Margin = new Thickness(0, 12, 0, 7);
                _presetsBox.Children.Add(cat);

                var wrap = new WrapPanel();
                foreach (var preset in group)
                {
                    var chip = BuildChip(preset);
                    chip.Margin = new Thickness(0, 0, 8, 8);
                    wrap.Children.Add(chip);
                    _chips[preset.Key] = chip;
                }
                _presetsBox.Children.Add(wrap);
            }
            left.Children.Add(_presetsBox);

            // ── MODE 2 : libre
            _libreRadio = MakeRadio();
            _libreTitle = Ui.P("MODE LIBRE — RÉGLAGES MANUELS", 12, "DimBrush", bold: true);
            var libreHeader = MakeModeHeader(_libreRadio, _libreTitle);
            libreHeader.Margin = new Thickness(0, 20, 0, 8);
            libreHeader.MouseLeftButtonUp += (s, e) => SwitchToLibreMode();
            left.Children.Add(libreHeader);

            _libreBox = new StackPanel();
            _fsBox = new ComboBox { Style = Ui.S("Select"), Width = 110 };
            foreach (var fs in new[] { "FAT32", "exFAT", "NTFS" }) _fsBox.Items.Add(fs);
            _fsBox.SelectionChanged += (s, e) => OnManualChanged();

            _clusterBox = new ComboBox { Style = Ui.S("Select"), Width = 110, Margin = new Thickness(10, 0, 0, 0) };
            foreach (var c in new[] { "Auto", "4 Ko", "8 Ko", "16 Ko", "32 Ko", "64 Ko" }) _clusterBox.Items.Add(c);
            _clusterBox.SelectionChanged += (s, e) => OnManualChanged();

            _nameBox = new TextBox { Style = Ui.S("Input"), Width = 300, Margin = new Thickness(10, 0, 0, 0), MaxLength = 11 };
            var v0 = _disk.Volumes.Count > 0 ? _disk.Volumes[0] : null;
            _nameBox.Text = v0 != null && v0.IsReady ? v0.Label : "";
            _nameBox.GotKeyboardFocus += (s, e) => { if (_preset != null) SwitchToLibreMode(); };

            var manualRow = Ui.HStack(0, _fsBox, _clusterBox,
                Ui.HStack(8, new Border(), Ui.T("Nom :", 18, "LavenderBrush")), _nameBox);
            _libreBox.Children.Add(manualRow);

            var manualNote = Ui.T("Un seul mode à la fois : clique ici (ou modifie un réglage) pour passer en libre, clique une console pour revenir aux presets.", 15, "DimBrush", wrap: true);
            manualNote.Margin = new Thickness(0, 8, 0, 0);
            _libreBox.Children.Add(manualNote);
            left.Children.Add(_libreBox);

            var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(leftScroll, 0);
            body.Children.Add(leftScroll);

            _sideBox = new StackPanel();
            var sideScroll = new ScrollViewer { Content = _sideBox, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(sideScroll, 1);
            body.Children.Add(sideScroll);

            Grid.SetRow(body, 1);
            root.Children.Add(body);
            return root;
        }

        // ── Bascule de mode ────────────────────────────────────────────────
        private static Border MakeRadio()
        {
            return new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        private static StackPanel MakeModeHeader(Border radio, TextBlock title)
        {
            var h = Ui.HStack(10, radio, title);
            h.Cursor = System.Windows.Input.Cursors.Hand;
            h.Background = Brushes.Transparent; // zone cliquable pleine
            return h;
        }

        private void SwitchToPresetMode()
        {
            if (_preset != null) return;
            _preset = _lastPreset ?? ConsolePresets.Find("wii");
            SyncManualControls();
            RestyleChips();
            RebuildSide();
            UpdateModeVisuals();
        }

        private void SwitchToLibreMode()
        {
            if (_preset == null) return;
            _lastPreset = _preset;
            _preset = null;
            RestyleChips();
            RebuildSide();
            UpdateModeVisuals();
        }

        private void UpdateModeVisuals()
        {
            bool presetMode = _preset != null;

            _presetRadio.BorderBrush = Ui.B(presetMode ? "GoldBrush" : "DimBorderBrush");
            _presetRadio.Background = presetMode ? Ui.B("GoldBrush") : Brushes.Transparent;
            _presetTitle.Foreground = Ui.B(presetMode ? "GoldBrush" : "DimBrush");
            _presetsBox.Opacity = presetMode ? 1.0 : 0.4;

            _libreRadio.BorderBrush = Ui.B(!presetMode ? "GoldBrush" : "DimBorderBrush");
            _libreRadio.Background = !presetMode ? Ui.B("GoldBrush") : Brushes.Transparent;
            _libreTitle.Foreground = Ui.B(!presetMode ? "GoldBrush" : "DimBrush");
            _libreBox.Opacity = !presetMode ? 1.0 : 0.4;
        }

        // ── Chips presets ──────────────────────────────────────────────────
        private Button BuildChip(ConsolePreset preset)
        {
            var btn = new Button
            {
                Style = Ui.S("FootChip"),
                Padding = new Thickness(11, 3, 11, 4),
                Tag = preset,
            };
            btn.Click += (s, e) =>
            {
                _preset = _lastPreset = preset;
                SyncManualControls();
                RestyleChips();
                RebuildSide();
                UpdateModeVisuals();
            };
            return btn;
        }

        private void RestyleChips()
        {
            foreach (var kv in _chips)
            {
                var preset = (ConsolePreset)kv.Value.Tag;
                bool sel = _preset != null && _preset.Key == preset.Key;
                string shortName = ChipLabel(preset);
                if (!preset.CanFormat)
                {
                    kv.Value.BorderBrush = Ui.B(sel ? "GoldBrush" : "DimBorderBrush");
                    kv.Value.Background = Ui.B("PanelDarkBrush");
                    kv.Value.Content = Ui.HStack(6,
                        Ui.Icon(Ui.GeoInfo, sel ? "GoldBrush" : "DimBrush", 13),
                        Ui.T(shortName, 17, sel ? "GoldBrush" : "DimBrush"));
                }
                else
                {
                    kv.Value.BorderBrush = Ui.B(sel ? "GoldBrush" : "DimBorderBrush");
                    kv.Value.Background = Ui.B("PanelBrush");
                    kv.Value.BorderThickness = new Thickness(2);
                    kv.Value.Content = Ui.T((sel ? "▶ " : "") + shortName, 17, sel ? "GoldBrush" : "BrightBrush");
                }
            }
        }

        private static string ChipLabel(ConsolePreset p)
        {
            switch (p.Key)
            {
                case "nes": return "NES";
                case "snes": return "SNES";
                case "n64": return "N64";
                case "gamecube": return "GameCube";
                case "wii": return "Wii";
                case "wiiu": return "Wii U";
                case "switch": return "Switch";
                case "gb": return "GB / GBA";
                case "ds": return "DS / DSi";
                case "3ds": return "3DS";
                case "ps1": return "PS1";
                case "ps2": return "PS2";
                case "ps3": return "PS3";
                case "ps4": return "PS4";
                case "psp": return "PSP";
                case "vita": return "Vita / PSTV";
                case "sms": return "Master System / GG";
                case "megadrive": return "Mega Drive";
                case "saturn": return "Saturn";
                case "gdemu": return "Dreamcast";
                case "xbox": return "Xbox";
                case "xbox360": return "Xbox 360";
                case "xboxone": return "Xbox One";
                case "atari": return "Atari 2600 / 7800";
                case "jaguar": return "Jaguar";
                case "lynx": return "Lynx";
                case "pce": return "PC-Engine";
                case "neogeo": return "Neo Geo";
                case "everdrive": return "EverDrive +";
                case "mister": return "MiSTer";
                case "batocera": return "Batocera";
                case "pc": return "PC";
                default: return p.Name;
            }
        }

        // ── Synchronisation preset ↔ contrôles manuels ─────────────────────
        private void SyncManualControls()
        {
            _updating = true;
            if (_preset != null && _preset.CanFormat)
            {
                _fsBox.SelectedIndex = _preset.Fs == TargetFs.Fat32 ? 0 : _preset.Fs == TargetFs.ExFat ? 1 : 2;
                _clusterBox.SelectedIndex = ClusterIndex(_preset.ClusterBytes);
            }
            else if (_fsBox.SelectedIndex < 0)
            {
                _fsBox.SelectedIndex = 0;
                _clusterBox.SelectedIndex = ClusterIndex(32 << 10);
            }
            _updating = false;
        }

        private static int ClusterIndex(int bytes)
        {
            switch (bytes)
            {
                case 4 << 10: return 1;
                case 8 << 10: return 2;
                case 16 << 10: return 3;
                case 32 << 10: return 4;
                case 64 << 10: return 5;
                default: return 0; // Auto
            }
        }

        private void OnManualChanged()
        {
            if (_updating) return;
            if (_preset != null) { _lastPreset = _preset; _preset = null; }
            RestyleChips();
            RebuildSide();
            UpdateModeVisuals();
        }

        private TargetFs SelectedFs()
        {
            switch (_fsBox.SelectedIndex)
            {
                case 1: return TargetFs.ExFat;
                case 2: return TargetFs.Ntfs;
                default: return TargetFs.Fat32;
            }
        }

        private int SelectedClusterBytes()
        {
            switch (_clusterBox.SelectedIndex)
            {
                case 1: return 4 << 10;
                case 2: return 8 << 10;
                case 3: return 16 << 10;
                case 4: return 32 << 10;
                case 5: return 64 << 10;
                default: return 0; // Auto
            }
        }

        // ── Colonne droite ─────────────────────────────────────────────────
        private void RebuildSide()
        {
            _sideBox.Children.Clear();

            if (_preset != null && !_preset.CanFormat)
            {
                var info = new StackPanel();
                info.Children.Add(Ui.P(_preset.Name.ToUpperInvariant(), 13, "GoldBrush", bold: true));
                var head = Ui.HStack(8, Ui.Icon(Ui.GeoInfo, "OrangeBrush", 16), Ui.P("PAS DE FORMATAGE PC POUR CE CAS", 10, "OrangeBrush", bold: true));
                head.Margin = new Thickness(0, 10, 0, 8);
                info.Children.Add(head);
                info.Children.Add(Ui.T(_preset.Pedagogy, 18, "LavenderBrush", wrap: true));
                var card = Ui.Card(info, "OrangeBrush");
                card.Margin = new Thickness(0, 0, 0, 14);
                _sideBox.Children.Add(card);

                var backBtn = Ui.Btn("◀ CHOISIR UNE AUTRE CONSOLE", "BtnOutline", 11);
                backBtn.Click += (s, e) =>
                {
                    _preset = _lastPreset != null && _lastPreset.CanFormat ? _lastPreset : ConsolePresets.Find("wii");
                    SyncManualControls();
                    RestyleChips();
                    RebuildSide();
                    UpdateModeVisuals();
                };
                _sideBox.Children.Add(backBtn);
                return;
            }

            TargetFs fs = SelectedFs();
            int cluster = SelectedClusterBytes();
            string fsName = fs == TargetFs.Fat32 ? "FAT32" : fs == TargetFs.ExFat ? "exFAT" : "NTFS";
            string clusterName = cluster > 0 ? $"{cluster >> 10} Ko" : "Auto";

            var top = new StackPanel();
            top.Children.Add(Ui.P(_preset != null ? _preset.Name.ToUpperInvariant() : "MODE LIBRE", 13, "GoldBrush", bold: true));
            var chipRow = Ui.HStack(8,
                Ui.Chip(fsName, "GoldBrush", 17),
                Ui.Chip(clusterName, "GoldBrush", 17),
                Ui.Chip("MBR", "GoldBrush", 17));
            chipRow.Margin = new Thickness(0, 10, 0, 12);
            top.Children.Add(chipRow);

            var whyHead = Ui.HStack(8, Ui.Icon(Ui.GeoInfo, "CyanBrush", 15), Ui.P("POURQUOI CES RÉGLAGES ?", 11, "CyanBrush"));
            var whyText = Ui.T(
                _preset != null
                    ? _preset.Pedagogy
                    : "Mode libre : tu choisis tout. Rappels utiles — FAT32 = compatible consoles mais fichiers de 4 Go max ; exFAT = gros fichiers OK mais refusé par la plupart des consoles rétro ; NTFS = monde Windows.",
                18, "LavenderBrush", wrap: true);
            whyText.Margin = new Thickness(0, 8, 0, 0);
            var why = new StackPanel();
            why.Children.Add(whyHead);
            why.Children.Add(whyText);
            var whyCard = new Border
            {
                Background = Ui.B("PanelDarkBrush"),
                BorderBrush = Ui.B("DimBorderBrush"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(12, 10, 12, 10),
                Child = why,
            };
            top.Children.Add(whyCard);

            var presetCard = Ui.Card(top, "GoldBrush");
            presetCard.Margin = new Thickness(0, 0, 0, 14);
            _sideBox.Children.Add(presetCard);

            // option : effacement complet avant formatage (l'équivalent du « formatage non rapide »)
            var checkBox = new Border
            {
                Width = 18,
                Height = 18,
                BorderThickness = new Thickness(2),
                BorderBrush = Ui.B(_zeroFirst ? "GoldBrush" : "PeriBrush"),
                Background = _zeroFirst ? Ui.B("GoldBrush") : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var zeroText = Ui.T("Formatage complet : zéros sur 100 % du support avant (rien de récupérable). Long : ~1 h pour 60 Go. Décoché : rapide (recommandé).", 17, _zeroFirst ? "BrightBrush" : "DimBrush", wrap: true);
            zeroText.VerticalAlignment = VerticalAlignment.Center;
            var zeroGrid = new Grid();
            zeroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            zeroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            checkBox.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(checkBox, 0);
            Grid.SetColumn(zeroText, 1);
            zeroGrid.Children.Add(checkBox);
            zeroGrid.Children.Add(zeroText);
            var zeroPanel = new Border
            {
                Background = Ui.B("PanelDarkBrush"),
                BorderBrush = Ui.B(_zeroFirst ? "GoldBrush" : "DimBorderBrush"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = zeroGrid,
            };
            zeroPanel.MouseLeftButtonUp += (s, e) => { _zeroFirst = !_zeroFirst; RebuildSide(); };
            _sideBox.Children.Add(zeroPanel);

            // avertissement
            string letter = _disk.Volumes.Count > 0 ? _disk.Volumes[0].Letter : $"le disque {_disk.Index}";
            var warnText = Ui.T($"Tout le contenu de {letter} sera effacé. Une confirmation te sera demandée avant le lancement.", 17, "RedBrush", wrap: true);
            warnText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0xC0));
            warnText.MaxWidth = 320;
            var warn = Ui.HStack(10, Ui.Icon(Ui.GeoWarn, "RedBrush", 20), warnText);
            var warnBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x12, 0x20)),
                BorderBrush = Ui.B("RedBrush"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Child = warn,
            };
            _sideBox.Children.Add(warnBorder);

            var go = Ui.Btn("▶ FORMATER MAINTENANT", "BtnGold", 14);
            go.Click += (s, e) => LaunchFormat(fs, cluster);
            _sideBox.Children.Add(go);

            var eta = Ui.T(_zeroFirst
                ? "Effacement complet + formatage : long — compte ~1 h pour 60 Go de clé USB"
                : fs == TargetFs.Fat32 ? "Formatage rapide — quelques secondes" : "Formatage rapide par Windows — moins d'une minute", 15, "DimBrush");
            eta.Margin = new Thickness(0, 8, 0, 0);
            eta.HorizontalAlignment = HorizontalAlignment.Center;
            _sideBox.Children.Add(eta);
        }

        // ── Lancement ──────────────────────────────────────────────────────
        private void LaunchFormat(TargetFs fs, int cluster)
        {
            if (!_main.EnsureAdmin()) return;

            string name = (_nameBox.Text ?? "").Trim();

            // garde-fou Wii U : l'étiquette « WIIU » casse le homebrew
            if (_preset != null && _preset.Key == "wiiu" && name.ToUpperInvariant() == "WIIU")
            {
                Dialogs.Info(Window.GetWindow(this), "NOM À ÉVITER",
                    "Sur Wii U, une carte SD étiquetée « WIIU » casse le homebrew (conflit avec le dossier wiiu).\nChoisis un autre nom — je l'ai vidé pour toi.");
                _nameBox.Text = "";
                return;
            }

            string fsName = fs == TargetFs.Fat32 ? "FAT32" : fs == TargetFs.ExFat ? "exFAT" : "NTFS";
            string what = _preset != null ? $"préréglage {_preset.Name}" : "mode libre";
            string zeroNote = _zeroFirst ? "\nEffacement complet demandé : zéros sur 100 % de la surface d'abord (long)." : "";
            if (!Dialogs.Confirm(Window.GetWindow(this), "DERNIÈRE CHANCE",
                $"« {_disk.Model} » ({ByteFormatter.Decimal(_disk.SizeBytes)}) va être formaté en {fsName} — {what}.\nTOUT SON CONTENU SERA DÉFINITIVEMENT EFFACÉ.{zeroNote}",
                "▶ FORMATER", danger: true)) return;

            var disk = _disk;
            bool zeroFirst = _zeroFirst;
            var pv = new ProgressView(_main, zeroFirst ? "▶ EFFACEMENT COMPLET + FORMATAGE" : "▶ FORMATAGE EN COURS",
                $"{disk.Model} · {ByteFormatter.Decimal(disk.SizeBytes)} · {fsName}", showBar: false);
            _main.ShowProgress(pv);
            pv.StartWork((log, ct) => DoFormat(disk, fs, cluster, name, zeroFirst, log, ct), "FORMATAGE TERMINÉ");
        }

        private static string DoFormat(PhysicalDisk disk, TargetFs fs, int cluster, string name, bool zeroFirst,
                                       Action<string> log, CancellationToken ct)
        {
            string zeroSummary = "";
            if (zeroFirst)
            {
                log("Effacement complet : zéros sur 100 % de la surface…");
                var wr = Core.Wipe.Wiper.FullWipe(disk, log, ct);
                zeroSummary = $"Zéros : {ByteFormatter.Decimal(wr.BytesWritten)} en {wr.Duration.TotalMinutes:F0} min"
                    + (wr.WriteErrors > 0 ? $" ({wr.WriteErrors} erreurs d'écriture !)" : "") + "\n";
            }
            if (fs == TargetFs.Fat32)
            {
                var opt = new Fat32FormatOptions
                {
                    ClusterBytes = cluster > 0 ? cluster : 32 << 10,
                    Label = name,
                };
                var rep = Fat32Formatter.FormatDisk(disk, opt, log, ct);
                string mounted = WaitMount(disk.Index, log);
                return $"{zeroSummary}FAT32 · clusters {rep.ClusterBytes >> 10} Ko · {rep.ClusterCount:N0} clusters · {rep.Duration.TotalSeconds:F1} s{mounted}";
            }
            else
            {
                var dur = WindowsFormatter.PrepareAndFormatDisk(disk, fs, name, cluster, log, ct);
                string mounted = WaitMount(disk.Index, log);
                return $"{zeroSummary}{(fs == TargetFs.ExFat ? "exFAT" : "NTFS")} posé par le formateur Windows en {dur.TotalSeconds:F1} s{mounted}";
            }
        }

        private static string WaitMount(int diskIndex, Action<string> log)
        {
            log("Attente du montage Windows…");
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(500);
                var again = DiskEnumerator.GetDisks().FirstOrDefault(d => d.Index == diskIndex);
                if (again != null && again.Volumes.Count > 0 && again.Volumes[0].IsReady)
                {
                    var v = again.Volumes[0];
                    return $"\nMonté : {v.Letter} · {v.FileSystem}" + (string.IsNullOrEmpty(v.Label) ? "" : $" · « {v.Label} »");
                }
            }
            return "\nVolume pas encore monté — débranche et rebranche le support si besoin.";
        }
    }
}
