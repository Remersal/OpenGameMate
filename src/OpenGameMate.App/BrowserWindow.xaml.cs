using System;
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

    public void ShowForUser(bool activate = true)
    {
        if (!IsVisible)
        {
            ShowActivated = activate;
            Show();
            ShowActivated = true;
        }

        WindowState = WindowState.Normal;
        if (activate)
        {
            Activate();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        BrowserView.Dispose();
        base.OnClosed(e);
    }
}
