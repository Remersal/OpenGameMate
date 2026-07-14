using System;
using System.Drawing;
using Forms = System.Windows.Forms;

namespace OpenGameMate.App;

public sealed class TrayIconController : IDisposable
{
    private readonly Icon _applicationIcon;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _showMain;
    private readonly Forms.ToolStripMenuItem _showBrowser;
    private readonly Forms.ToolStripMenuItem _hideBrowser;
    private readonly Forms.ToolStripMenuItem _sendNow;
    private readonly Forms.ToolStripMenuItem _pauseResume;
    private readonly Forms.ToolStripMenuItem _stop;
    private readonly Forms.ToolStripMenuItem _exit;

    public TrayIconController(
        Action showMain,
        Action showBrowser,
        Action hideBrowser,
        Action sendNow,
        Action pauseResume,
        Action stop,
        Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        _showMain = menu.Items.Add("Show OpenGameMate", null, (_, _) => showMain()) as Forms.ToolStripMenuItem
            ?? throw new InvalidOperationException("Unable to create tray menu item.");
        _showBrowser = menu.Items.Add("Show window", null, (_, _) => showBrowser()) as Forms.ToolStripMenuItem
            ?? throw new InvalidOperationException("Unable to create tray menu item.");
        _hideBrowser = menu.Items.Add("Hide to tray", null, (_, _) => hideBrowser()) as Forms.ToolStripMenuItem
            ?? throw new InvalidOperationException("Unable to create tray menu item.");
        menu.Items.Add(new Forms.ToolStripSeparator());
        _sendNow = menu.Items.Add("Send now", null, (_, _) => sendNow()) as Forms.ToolStripMenuItem
            ?? throw new InvalidOperationException("Unable to create tray menu item.");
        _pauseResume = menu.Items.Add("Pause", null, (_, _) => pauseResume()) as Forms.ToolStripMenuItem
            ?? throw new InvalidOperationException("Unable to create tray menu item.");
        _stop = menu.Items.Add("Stop", null, (_, _) => stop()) as Forms.ToolStripMenuItem
            ?? throw new InvalidOperationException("Unable to create tray menu item.");
        menu.Items.Add(new Forms.ToolStripSeparator());
        _exit = menu.Items.Add("Exit", null, (_, _) => exit()) as Forms.ToolStripMenuItem
            ?? throw new InvalidOperationException("Unable to create tray menu item.");

        var processPath = Environment.ProcessPath;
        _applicationIcon = processPath is null
            ? (Icon)SystemIcons.Application.Clone()
            : Icon.ExtractAssociatedIcon(processPath) ?? (Icon)SystemIcons.Application.Clone();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "OpenGameMate",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => showMain();
    }

    public void Update(
        bool chinese,
        string state,
        bool browserAvailable,
        bool canSend,
        bool canPause,
        bool paused,
        bool canStop)
    {
        _notifyIcon.Text = $"OpenGameMate - {state}"[..Math.Min(63, $"OpenGameMate - {state}".Length)];
        _showMain.Text = chinese ? "显示主界面" : "Show OpenGameMate";
        _showBrowser.Text = chinese ? "显示主窗口" : "Show window";
        _hideBrowser.Text = chinese ? "隐藏到托盘" : "Hide to tray";
        _sendNow.Text = chinese ? "立即发送" : "Send now";
        _pauseResume.Text = paused
            ? chinese ? "恢复" : "Resume"
            : chinese ? "暂停" : "Pause";
        _stop.Text = chinese ? "停止" : "Stop";
        _exit.Text = chinese ? "退出" : "Exit";
        _showBrowser.Enabled = browserAvailable;
        _hideBrowser.Enabled = browserAvailable;
        _sendNow.Enabled = canSend;
        _pauseResume.Enabled = canPause;
        _stop.Enabled = canStop;
    }

    public void Notify(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _applicationIcon.Dispose();
    }
}
