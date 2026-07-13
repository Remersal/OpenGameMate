using System.Configuration;
using System.Data;
using System.Windows;

namespace OpenGameMate.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        try
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"OpenGameMate Phase 0 启动失败：{exception.GetType().Name}\nHRESULT: 0x{exception.HResult:X8}",
                "OpenGameMate Phase 0",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}

