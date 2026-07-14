using System;
using System.Windows;
using OpenGameMate.Configuration;

namespace OpenGameMate.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        try
        {
            var paths = AppDataPaths.Resolve(e.Args, AppContext.BaseDirectory);
            MainWindow = new MainWindow(paths);
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"{ProductMetadata.DisplayName} failed to start / 启动失败：{exception.GetType().Name}\nHRESULT: 0x{exception.HResult:X8}",
                ProductMetadata.DisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
