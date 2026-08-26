#nullable disable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VintageDrive.App.Views
{
    /// <summary>Petite boîte à outils du thème pixel : textes, chips, cartes, icônes.</summary>
    internal static class Ui
    {
        public static Brush B(string key) => (Brush)Application.Current.FindResource(key);
        public static FontFamily PxFont => (FontFamily)Application.Current.FindResource("PxFont");
        public static FontFamily MonoFont => (FontFamily)Application.Current.FindResource("MonoFont");
        public static Style S(string key) => (Style)Application.Current.FindResource(key);

        /// <summary>Texte VT323 (corps, données).</summary>
        public static TextBlock T(string text, double size, string brushKey, bool wrap = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = MonoFont,
                FontSize = size,
                Foreground = B(brushKey),
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (wrap)
            {
                tb.TextWrapping = TextWrapping.Wrap;
                tb.LineHeight = size * 1.18;
                tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
            TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);
            return tb;
        }

        /// <summary>Texte Silkscreen (titres, labels), rendu net.</summary>
        public static TextBlock P(string text, double size, string brushKey, bool bold = false, double spacing = 0)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = PxFont,
                FontSize = size,
                Foreground = B(brushKey),
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);
            return tb;
        }

        /// <summary>Grand titre pixel avec ombre dure (duplication décalée, sans flou).</summary>
        public static Grid HardTitle(string text, double size, string brushKey = "BrightBrush", double offset = 3)
        {
            var shadow = P(text, size, "BgBrush", bold: true);
            shadow.Foreground = Brushes.Black;
            shadow.Margin = new Thickness(offset, offset, 0, 0);
            var face = P(text, size, brushKey, bold: true);
            var g = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            g.Children.Add(shadow);
            g.Children.Add(face);
            return g;
        }

        /// <summary>Chip bordée (tags NVMe, USB, MBR…).</summary>
        public static Border Chip(string text, string brushKey, double size = 15, string bgKey = null)
        {
            return new Border
            {
                BorderBrush = B(brushKey),
                BorderThickness = new Thickness(1),
                Background = bgKey != null ? B(bgKey) : Brushes.Transparent,
                Padding = new Thickness(8, 0, 8, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = T(text, size, brushKey),
            };
        }

        /// <summary>Badge pixel (SYSTÈME, DISQUE INTERNE, SÉLECTIONNÉ…).</summary>
        public static Border Badge(string text, string brushKey, bool filled = false)
        {
            var tb = P(text, 10, filled ? "GoldTextBrush" : brushKey, bold: true);
            return new Border
            {
                BorderBrush = B(brushKey),
                BorderThickness = new Thickness(filled ? 0 : 2),
                Background = filled ? B(brushKey) : Brushes.Transparent,
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = tb,
            };
        }

        /// <summary>Panneau pixel à ombre dure.</summary>
        public static ContentControl Card(UIElement content, string borderKey = "DimBorderBrush",
                                          string bgKey = "PanelBrush", Thickness? padding = null)
        {
            var c = new ContentControl { Content = content, Style = S("Card") };
            c.SetResourceReference(Control.BorderBrushProperty, borderKey);
            c.SetResourceReference(Control.BackgroundProperty, bgKey);
            if (padding.HasValue) c.Padding = padding.Value;
            return c;
        }

        /// <summary>Bouton pixel dans l'un des trois styles (BtnGold, BtnOutline, BtnDanger).</summary>
        public static Button Btn(string text, string styleKey, double size = 13)
        {
            return new Button { Content = text, Style = S(styleKey), FontSize = size };
        }

        /// <summary>Icône vectorielle (trait, pas d'emoji).</summary>
        public static Path Icon(string data, string brushKey, double size = 20, double thickness = 2)
        {
            return new Path
            {
                Data = Geometry.Parse(data),
                Stroke = B(brushKey),
                StrokeThickness = thickness,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        // Géométries maison (grille 24)
        public const string GeoUsb = "M8,2 L16,2 L16,7 L8,7 Z M6,7 L18,7 L18,21 L6,21 Z M10,4.5 L11,4.5 M13,4.5 L14,4.5";
        public const string GeoHdd = "M2,7 L22,7 L22,17 L2,17 Z M16,12 A1.6,1.6 0 1 0 19.2,12 A1.6,1.6 0 1 0 16,12 M5,12 L11,12";
        public const string GeoLock = "M5,11 L19,11 L19,20 L5,20 Z M8,11 L8,8 A4,4 0 0 1 16,8 L16,11";
        public const string GeoInfo = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,11 L12,16 M12,7.5 L12,8";
        public const string GeoWarn = "M12,3 L2.5,20 L21.5,20 Z M12,10 L12,14 M12,16.5 L12,17";
        public const string GeoCheck = "M4,13 L9,18 L20,6";
        public const string GeoCross = "M6,6 L18,18 M18,6 L6,18";
        public const string GeoSave = "M12,4 L12,15 M7,10 L12,15 L17,10 M4,19 L20,19";
        public const string GeoPalette = "M12,3 A9,9 0 1 0 12,21 C10.5,19 12.5,17.5 14.5,17.8 C17.1,18.2 18.5,16.8 18.7,15";

        /// <summary>Empile horizontalement avec un espacement régulier.</summary>
        public static StackPanel HStack(double gap, params UIElement[] children)
            => Stack(Orientation.Horizontal, gap, children);

        /// <summary>Empile verticalement avec un espacement régulier.</summary>
        public static StackPanel VStack(double gap, params UIElement[] children)
            => Stack(Orientation.Vertical, gap, children);

        private static StackPanel Stack(Orientation o, double gap, UIElement[] children)
        {
            var sp = new StackPanel { Orientation = o };
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] == null) continue;
                if (i < children.Length - 1 && children[i] is FrameworkElement fe)
                {
                    var m = fe.Margin;
                    fe.Margin = o == Orientation.Horizontal
                        ? new Thickness(m.Left, m.Top, m.Right + gap, m.Bottom)
                        : new Thickness(m.Left, m.Top, m.Right, m.Bottom + gap);
                }
                sp.Children.Add(children[i]);
            }
            return sp;
        }

        /// <summary>Pousse un élément à droite dans un DockPanel.</summary>
        public static DockPanel Spread(UIElement left, UIElement right)
        {
            var dp = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(left, Dock.Left);
            DockPanel.SetDock(right, Dock.Right);
            dp.Children.Add(left);
            dp.Children.Add(right);
            return dp;
        }
    }
}
