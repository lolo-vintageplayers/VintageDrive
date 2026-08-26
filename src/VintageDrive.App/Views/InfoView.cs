#nullable disable
using System.Windows;
using System.Windows.Controls;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Util;

namespace VintageDrive.App.Views
{
    /// <summary>Écran « Informations » : identité complète, table de partitions, volumes et occupation.</summary>
    public class InfoView : UserControl
    {
        private readonly MainWindow _main;
        private readonly PhysicalDisk _disk;

        public InfoView(MainWindow main, PhysicalDisk disk)
        {
            _main = main;
            _disk = disk;
            Content = Build(DiskInspector.GetDetails(disk));
        }

        private UIElement Build(DiskDetails details)
        {
            var root = new StackPanel { Margin = new Thickness(28, 22, 28, 12) };

            // ── En-tête
            var back = Ui.Btn("◀", "BtnOutline", 13);
            back.Padding = new Thickness(12, 8, 12, 8);
            back.Click += (s, e) => _main.ShowDisks();
            var header = Ui.Spread(
                Ui.HStack(14, back, Ui.P($"INFORMATIONS — DISQUE {_disk.Index}", 15, "BrightBrush", bold: true)),
                Ui.T($"{_disk.Model}", 16, "DimBrush"));
            header.Margin = new Thickness(0, 0, 0, 16);
            root.Children.Add(header);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // ── Colonne gauche : identité
            var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var idPanel = new StackPanel();
            idPanel.Children.Add(Ui.P("▶ IDENTITÉ", 12, "GoldBrush", bold: true));
            AddRow(idPanel, "MODÈLE", _disk.Model);
            AddRow(idPanel, "N° DE SÉRIE", string.IsNullOrEmpty(_disk.SerialNumber) ? "—" : _disk.SerialNumber);
            AddRow(idPanel, "FIRMWARE", string.IsNullOrEmpty(_disk.FirmwareRevision) ? "—" : _disk.FirmwareRevision);
            AddRow(idPanel, "BUS", _disk.Bus.ToString().ToUpperInvariant() + (_disk.IsRemovableMedia ? " · média amovible" : " · média fixe"));
            AddRow(idPanel, "SECTEUR LOGIQUE", _disk.BytesPerSector + " octets");
            AddRow(idPanel, "TABLE DE PARTITIONS", details.PartitionStyle);
            AddRow(idPanel, "TAILLE EXACTE", $"{_disk.SizeBytes:N0} octets");
            AddRow(idPanel, "EN GO (VENDEUR)", ByteFormatter.Decimal(_disk.SizeBytes) + "  — les fabricants comptent en milliards d'octets");
            AddRow(idPanel, "EN GIO (WINDOWS)", ByteFormatter.Binary(_disk.SizeBytes) + "  — Windows compte en puissances de 1024 : rien ne « manque »");
            var idCard = Ui.Card(idPanel, "PeriBrush");
            idCard.Margin = new Thickness(0, 0, 0, 14);
            left.Children.Add(idCard);
            Grid.SetColumn(left, 0);
            columns.Children.Add(left);

            // ── Colonne droite : partitions puis volumes
            var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };

            var partPanel = new StackPanel();
            partPanel.Children.Add(Ui.P($"▶ PARTITIONS ({details.Partitions.Count})", 12, "GoldBrush", bold: true));
            if (details.Partitions.Count == 0)
            {
                var none = Ui.T("Aucune partition — support RAW, à formater avant usage.", 18, "LavenderBrush", wrap: true);
                none.Margin = new Thickness(0, 10, 0, 0);
                partPanel.Children.Add(none);
            }
            foreach (var p in details.Partitions)
            {
                var line1 = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 10, 0, 0) };
                var leftBits = Ui.HStack(8,
                    Ui.Badge("N° " + p.Number, "PeriBrush"),
                    Ui.T(p.TypeName + (p.GptName.Length > 0 ? $" · « {p.GptName} »" : ""), 19, "BrightBrush"));
                DockPanel.SetDock(leftBits, Dock.Left);
                line1.Children.Add(leftBits);
                var rightBits = Ui.HStack(8,
                    p.Letter.Length > 0 ? Ui.Chip(p.Letter, "CyanBrush", 16) : Ui.Chip("non montée", "DimBrush", 15),
                    Ui.T(ByteFormatter.Decimal(p.LengthBytes), 20, "GoldBrush"));
                DockPanel.SetDock(rightBits, Dock.Right);
                line1.Children.Add(rightBits);
                partPanel.Children.Add(line1);
                partPanel.Children.Add(Ui.T($"    début à {ByteFormatter.Decimal(p.OffsetBytes)}" + (p.IsBootFlagged ? " · marquée amorçable" : ""), 15, "DimBrush"));
            }
            if (details.Partitions.Count > 1)
            {
                var warn = Ui.HStack(10,
                    Ui.Icon(Ui.GeoWarn, "OrangeBrush", 18),
                    Ui.T("Plusieurs partitions ! Beaucoup de consoles et de loaders (USB Loader GX, OPL…) ne lisent que la PREMIÈRE. Un formatage VintageDrive recrée une partition unique et propre.", 17, "OrangeBrush", wrap: true));
                if (warn.Children[1] is FrameworkElement wt) wt.MaxWidth = 430;
                var warnBox = new Border
                {
                    BorderBrush = Ui.B("OrangeBrush"),
                    BorderThickness = new Thickness(2),
                    Background = Ui.B("PanelDarkBrush"),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 12, 0, 0),
                    Child = warn,
                };
                partPanel.Children.Add(warnBox);
            }
            var partCard = Ui.Card(partPanel, details.Partitions.Count > 1 ? "OrangeBrush" : "DimBorderBrush");
            partCard.Margin = new Thickness(0, 0, 0, 14);
            right.Children.Add(partCard);

            foreach (var v in details.Volumes)
            {
                var vp = new StackPanel();
                vp.Children.Add(Ui.P($"▶ VOLUME {v.Letter}" + (v.Label.Length > 0 ? $" — « {v.Label} »" : ""), 11, "CyanBrush", bold: true));
                AddRow(vp, "SYSTÈME DE FICHIERS", v.FileSystem + (v.ClusterBytes > 0 ? $" · clusters de {v.ClusterBytes >> 10} Ko" : ""));
                AddRow(vp, "N° DE SÉRIE DU VOLUME", string.IsNullOrEmpty(v.SerialHex) ? "—" : v.SerialHex);
                if (v.TotalBytes > 0)
                {
                    long used = v.TotalBytes - v.FreeBytes;
                    double frac = (double)used / v.TotalBytes;
                    var fill = new Border
                    {
                        Background = Ui.B("GoldBrush"),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Width = 0,
                    };
                    var track = new Border
                    {
                        BorderBrush = Ui.B("PeriBrush"),
                        BorderThickness = new Thickness(2),
                        Background = Ui.B("PanelDarkBrush"),
                        Padding = new Thickness(3),
                        Height = 24,
                        Margin = new Thickness(0, 10, 0, 6),
                        Child = fill,
                    };
                    track.SizeChanged += (s, e) => fill.Width = System.Math.Max(0, (track.ActualWidth - 10) * frac);
                    vp.Children.Add(track);
                    vp.Children.Add(Ui.T($"{ByteFormatter.Decimal(used)} utilisés · {ByteFormatter.Decimal(v.FreeBytes)} libres ({frac * 100:F0} % plein)", 18, "LavenderBrush"));
                }
                var vCard = Ui.Card(vp, "DimBorderBrush");
                vCard.Margin = new Thickness(0, 0, 0, 14);
                right.Children.Add(vCard);
            }

            Grid.SetColumn(right, 1);
            columns.Children.Add(right);
            root.Children.Add(columns);

            return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private static void AddRow(StackPanel panel, string label, string value)
        {
            var l = Ui.P(label, 9, "PeriBrush");
            l.Margin = new Thickness(0, 10, 0, 2);
            panel.Children.Add(l);
            panel.Children.Add(Ui.T(value, 19, "BrightBrush", wrap: true));
        }
    }
}
