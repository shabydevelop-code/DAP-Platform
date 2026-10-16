using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using GuideMe.Core.Localization;
using Forms = System.Windows.Forms;

namespace GuideMe.App;

/// <summary>
/// Session-scoped notification-area control. All WinForms objects are created
/// and disposed on the WPF UI dispatcher that owns the learner process.
/// </summary>
internal sealed class GuideSessionTray : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private Icon _trayIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _disposed;

    public GuideSessionTray(string guideId, IUiTextProvider texts, Action stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        var hebrew = texts.IsRightToLeft;
        _menu = new Forms.ContextMenuStrip();
        _menu.RightToLeft = hebrew ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;
        var heading = new Forms.ToolStripMenuItem("GuideMe") { Enabled = false };
        _menu.Items.Add(heading);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        var exit = new Forms.ToolStripMenuItem(texts.Get("Learner.Exit"));
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
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
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

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            var updated = ResolveIcon();
            var previous = _trayIcon;
            _trayIcon = updated;
            _icon.Icon = updated;
            previous.Dispose();
        });
    }

    private static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static Icon ResolveIcon()
    {
        // Use the original dark application icon on light taskbars.
        if (IsLightTaskbar())
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var darkIcon = Icon.ExtractAssociatedIcon(path);
                    if (darkIcon is not null) return darkIcon;
                }
                catch (IOException) { }
                catch (ArgumentException) { }
            }
        }
        // Use the original white tray icon on dark taskbars.
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
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _trayIcon.Dispose();
        _menu.Dispose();
    }
}
