using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace WRN.AIGateway
{
    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var xamlPath = Path.Combine(baseDir, "ui", "MainWindow.xaml");
            if (!File.Exists(xamlPath))
            {
                MessageBox.Show(
                    "WRN AI Gateway could not find its interface files. Please reinstall the application.",
                    "WRN AI Gateway",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            Window window;
            using (var stream = File.OpenRead(xamlPath))
            {
                window = (Window)XamlReader.Load(stream);
            }

            var controller = new AppController(window, baseDir);
            controller.Initialize();
            app.Run(window);
        }
    }
}
