#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace VintageDrive.App
{
    [DataContract]
    public class ThemeFile
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "author")] public string Author { get; set; }
        [DataMember(Name = "colors")] public Dictionary<string, string> Colors { get; set; }
    }

    /// <summary>
    /// Moteur de thèmes : le thème par défaut « Vintage Players » est dans App.xaml ;
    /// un fichier .vdtheme (JSON { name, author, colors }) peut remplacer les couleurs à chaud.
    /// Les vues se reconstruisent à la navigation, donc un retour à l'accueil suffit.
    /// </summary>
    public static class ThemeEngine
    {
        public static string CurrentName { get; private set; } = "Vintage Players";

        public static readonly string[] ColorKeys =
        {
            "Bg", "Panel", "PanelDark", "Peri", "DimBorder", "Gold", "GoldText",
            "Lavender", "Dim", "Bright", "Magenta", "Cyan", "Red", "Green", "Orange",
        };

        private static readonly Dictionary<string, string> Default = new Dictionary<string, string>
        {
            { "Bg", "#0A0A1E" }, { "Panel", "#191938" }, { "PanelDark", "#12122A" },
            { "Peri", "#7B7BE8" }, { "DimBorder", "#34346A" }, { "Gold", "#FFD23F" },
            { "GoldText", "#141428" }, { "Lavender", "#9D9DE8" }, { "Dim", "#6A6AA8" },
            { "Bright", "#EDEDFF" }, { "Magenta", "#FF2E97" }, { "Cyan", "#35E0E8" },
            { "Red", "#FF3B5C" }, { "Green", "#3EE873" }, { "Orange", "#FF9F2E" },
        };

        public static void ApplyDefault() => Apply("Vintage Players", Default);

        public static void Apply(string name, Dictionary<string, string> colors)
        {
            var res = Application.Current.Resources;
            foreach (var key in ColorKeys)
            {
                if (!colors.TryGetValue(key, out string hex)) continue;
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex.Trim());
                    res[key + "Brush"] = new SolidColorBrush(color);
                }
                catch (FormatException) { /* couleur illisible : on garde l'actuelle */ }
            }
            res["WindowGridBrush"] = BuildGridBrush(
                ((SolidColorBrush)res["BgBrush"]).Color,
                ((SolidColorBrush)res["PeriBrush"]).Color);
            CurrentName = string.IsNullOrWhiteSpace(name) ? "Sans nom" : name.Trim();
        }

        private static DrawingBrush BuildGridBrush(Color bg, Color line)
        {
            line.A = 0x12;
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(bg), null, new RectangleGeometry(new Rect(0, 0, 28, 28))));
            var lines = new GeometryGroup();
            lines.Children.Add(new LineGeometry(new Point(0, 0.5), new Point(28, 0.5)));
            lines.Children.Add(new LineGeometry(new Point(0.5, 0), new Point(0.5, 28)));
            group.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(line), 1), lines));
            return new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 28, 28),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
            };
        }

        public static Dictionary<string, string> CurrentColors()
        {
            var res = Application.Current.Resources;
            var colors = new Dictionary<string, string>();
            foreach (var key in ColorKeys)
                if (res[key + "Brush"] is SolidColorBrush b)
                    colors[key] = b.Color.ToString();
            return colors;
        }

        private static DataContractJsonSerializer MakeSerializer()
            => new DataContractJsonSerializer(typeof(ThemeFile),
                new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static void Export(string path)
        {
            var doc = new ThemeFile { Name = CurrentName, Author = "", Colors = CurrentColors() };
            using (var ms = new MemoryStream())
            {
                MakeSerializer().WriteObject(ms, doc);
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        /// <summary>Charge un .vdtheme ; lève une exception avec un message clair si illisible.</summary>
        public static void Import(string path)
        {
            ThemeFile doc;
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(path))))
                    doc = (ThemeFile)MakeSerializer().ReadObject(ms);
            }
            catch (Exception)
            {
                throw new InvalidDataException("Ce fichier n'est pas un thème VintageDrive (.vdtheme) valide.");
            }
            if (doc?.Colors == null || doc.Colors.Count == 0)
                throw new InvalidDataException("Aucune couleur lisible dans ce thème.");
            Apply(string.IsNullOrWhiteSpace(doc.Name) ? Path.GetFileNameWithoutExtension(path) : doc.Name, doc.Colors);
        }
    }
}
