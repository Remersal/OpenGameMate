using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace OpenGameMate.App;

public partial class BrowserWindow : Window
{
    public BrowserWindow()
    {
        InitializeComponent();
    }

    public WebView2 WebView => BrowserView;

    public void ShowForUser()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        BrowserView.Dispose();
        base.OnClosed(e);
    }
}
