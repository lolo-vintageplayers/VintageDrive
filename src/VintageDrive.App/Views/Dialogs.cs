#nullable disable
using System.Windows;
using System.Windows.Controls;
using VintageDrive.Core.Disks;
using VintageDrive.Core.Util;

namespace VintageDrive.App.Views
{
    public enum WipeKind { Cancel, Quick, Full }
    public enum TestKind { Cancel, Quick, Full }

    /// <summary>Boîtes de dialogue au thème pixel (construites en code).</summary>
    internal static class Dialogs
    {
        /// <summary>Affiche une fenêtre modale en assombrissant/floutant la fenêtre principale derrière.</summary>
        public static void ShowWithDim(Window owner, Window dialog)
        {
            var main = owner as MainWindow;
            main?.Dim(true);
            try { dialog.ShowDialog(); }
            finally { main?.Dim(false); }
        }

        private static Window MakeWindow(Window owner, string title, UIElement body, UIElement buttons, string borderKey)
        {
            var content = new StackPanel();
            content.Children.Add(Ui.P(title, 13, borderKey == "RedBrush" ? "RedBrush" : "GoldBrush", bold: true));
            if (body is FrameworkElement fe) fe.Margin = new Thickness(0, 12, 0, 16);
            content.Children.Add(body);
            content.Children.Add(buttons);

            var frame = new Border
            {
                Background = Ui.B("PanelBrush"),
                BorderBrush = Ui.B(borderKey),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(24, 18, 24, 18),
                Margin = new Thickness(0, 0, 5, 5),
            };
            var shadow = new Border { Background = System.Windows.Media.Brushes.Black, Margin = new Thickness(5, 5, 0, 0) };
            var grid = new Grid();
            grid.Children.Add(shadow);
            grid.Children.Add(frame);
            frame.Child = content;

            var win = new Window
            {
                Owner = owner,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                MaxWidth = 620,
                ShowInTaskbar = false,
                Content = grid,
            };
            win.MouseLeftButtonDown += (s, e) => { try { win.DragMove(); } catch { } };
            return win;
        }

        public static bool Confirm(Window owner, string title, string message, string confirmLabel, bool danger)
        {
            bool result = false;
            var msg = Ui.T(message, 18, "LavenderBrush", wrap: true);
            msg.MaxWidth = 520;

            var cancel = Ui.Btn("ANNULER", "BtnOutline", 11);
            var okBtn = Ui.Btn(confirmLabel, danger ? "BtnDanger" : "BtnGold", 11);
            okBtn.Margin = new Thickness(12, 0, 0, 0);
            var buttons = Ui.HStack(0, cancel, okBtn);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            var win = MakeWindow(owner, title, msg, buttons, danger ? "RedBrush" : "PeriBrush");
            cancel.Click += (s, e) => win.Close();
            okBtn.Click += (s, e) => { result = true; win.Close(); };
            ShowWithDim(owner, win);
            return result;
        }

        public static void Info(Window owner, string title, string message)
        {
            var msg = Ui.T(message, 18, "LavenderBrush", wrap: true);
            msg.MaxWidth = 520;
            var ok = Ui.Btn("OK", "BtnGold", 11);
            ok.HorizontalAlignment = HorizontalAlignment.Right;
            var win = MakeWindow(owner, title, msg, ok, "PeriBrush");
            ok.Click += (s, e) => win.Close();
            ShowWithDim(owner, win);
        }

        public static TestKind TestChoice(Window owner, PhysicalDisk disk)
        {
            var result = TestKind.Cancel;

            var body = new StackPanel { MaxWidth = 540 };
            body.Children.Add(Ui.T($"« {disk.Model} » · {ByteFormatter.Decimal(disk.SizeBytes)} annoncés — le test ÉCRIT sur tout le support : son contenu sera effacé, partitions comprises (reformatage en 10 s ensuite).", 18, "LavenderBrush", wrap: true));

            var quickTxt = Ui.T("TEST RAPIDE — quelques minutes (selon la vitesse du support, pas sa taille). Blocs signés échantillonnés sur 100 % de la plage : imparable pour démasquer une capacité falsifiée.", 16, "DimBrush", wrap: true);
            quickTxt.Margin = new Thickness(0, 10, 0, 4);
            body.Children.Add(quickTxt);
            body.Children.Add(Ui.T("TEST COMPLET — long (écrit puis relit 100 % de la surface, à la vitesse du support). La preuve absolue, octet par octet — et un effacement complet gratuit au passage.", 16, "DimBrush", wrap: true));

            var cancel = Ui.Btn("ANNULER", "BtnOutline", 10);
            var quick = Ui.Btn("▶ TEST RAPIDE", "BtnGold", 10);
            quick.Margin = new Thickness(12, 0, 0, 0);
            var full = Ui.Btn("TEST COMPLET", "BtnOutline", 10);
            full.Margin = new Thickness(12, 0, 0, 0);
            var buttons = Ui.HStack(0, cancel, quick, full);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            var win = MakeWindow(owner, "TESTER LA CAPACITÉ RÉELLE", body, buttons, "RedBrush");
            cancel.Click += (s, e) => win.Close();
            quick.Click += (s, e) => { result = TestKind.Quick; win.Close(); };
            full.Click += (s, e) => { result = TestKind.Full; win.Close(); };
            ShowWithDim(owner, win);
            return result;
        }

        public static WipeKind WipeChoice(Window owner, PhysicalDisk disk)
        {
            var result = WipeKind.Cancel;

            var body = new StackPanel { MaxWidth = 520 };
            body.Children.Add(Ui.T($"« {disk.Model} » · {ByteFormatter.Decimal(disk.SizeBytes)} — deux façons d'effacer :", 18, "LavenderBrush", wrap: true));

            var quickTxt = Ui.T("NETTOYAGE RAPIDE — quelques secondes. Zéros sur le début et la fin : détruit partitions et systèmes de fichiers. Débloque les supports récalcitrants.", 16, "DimBrush", wrap: true);
            quickTxt.Margin = new Thickness(0, 10, 0, 4);
            body.Children.Add(quickTxt);
            body.Children.Add(Ui.T("EFFACEMENT COMPLET — long (dépend du support). Zéros sur 100 % de la surface, une passe : pour la confidentialité avant revente ou don.", 16, "DimBrush", wrap: true));

            var cancel = Ui.Btn("ANNULER", "BtnOutline", 10);
            var quick = Ui.Btn("NETTOYAGE RAPIDE", "BtnGold", 10);
            quick.Margin = new Thickness(12, 0, 0, 0);
            var full = Ui.Btn("EFFACEMENT COMPLET", "BtnDanger", 10);
            full.Margin = new Thickness(12, 0, 0, 0);
            var buttons = Ui.HStack(0, cancel, quick, full);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;

            var win = MakeWindow(owner, "EFFACER LE SUPPORT", body, buttons, "RedBrush");
            cancel.Click += (s, e) => win.Close();
            quick.Click += (s, e) => { result = WipeKind.Quick; win.Close(); };
            full.Click += (s, e) => { result = WipeKind.Full; win.Close(); };
            ShowWithDim(owner, win);

            if (result == WipeKind.Full && !Confirm(owner, "EFFACEMENT COMPLET",
                $"Zéros sur 100 % de « {disk.Model} » — impossible à interrompre proprement une fois bien avancé, et long.\nOn y va ?",
                "▶ EFFACER TOUT", danger: true))
                result = WipeKind.Cancel;

            return result;
        }
    }
}
