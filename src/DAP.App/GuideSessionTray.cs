using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace DAP.App;

/// <summary>
/// Session-scoped notification-area control. All WinForms objects are created
/// and disposed on the WPF UI dispatcher that owns the learner process.
/// </summary>
internal sealed class GuideSessionTray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _trayIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _disposed;

    public GuideSessionTray(string guideId, string language, Action stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        var hebrew = string.Equals(language, "he", StringComparison.OrdinalIgnoreCase);
        _menu = new Forms.ContextMenuStrip();
        _menu.RightToLeft = hebrew ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;
        var heading = new Forms.ToolStripMenuItem("GuideMe") { Enabled = false };
        _menu.Items.Add(heading);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        var exit = new Forms.ToolStripMenuItem(hebrew ? "יציאה" : "Exit");
        exit.Click += (_, _) =>
        {
            exit.Enabled = false;
            stop();
        };
        _menu.Items.Add(exit);
        _trayIcon = ResolveIcon();
        _icon = new Forms.NotifyIcon
        {
            Text = "GuideMe",
            Icon = _trayIcon,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left && !_disposed && !_menu.Visible)
            {
                // Right-click is handled by NotifyIcon.ContextMenuStrip.
                // Show the same menu on a single left-click.
                _menu.Show(System.Windows.Forms.Cursor.Position);
            }
        };
    }

    private static Icon ResolveIcon()
    {
        // Use the Windows app icon embedded at build time, independently
        // of the browser extension assets.
        using (var stream = typeof(GuideSessionTray).Assembly.GetManifestResourceStream("GuideMe.TrayIcon.ico"))
        {
            if (stream is not null)
            {
                using var embeddedIcon = new Icon(stream);
                return (Icon)embeddedIcon.Clone();
            }
        }

        // Fallback only when the embedded resource is unavailable.
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(executable))
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(executable);
                if (icon is not null)
                    return icon;
            }
            catch (IOException) { }
            catch (ArgumentException) { }
        }
        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _trayIcon.Dispose();
        _menu.Dispose();
    }
}
