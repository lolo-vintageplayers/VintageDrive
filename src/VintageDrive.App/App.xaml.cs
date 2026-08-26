#nullable disable
using System.Windows;

namespace VintageDrive.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show("Erreur inattendue : " + args.Exception.Message, "VintageDrive",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
