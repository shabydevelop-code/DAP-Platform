using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
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
        var heading = new Forms.ToolStripMenuItem("GuideMe — " + guideId) { Enabled = false };
        _menu.Items.Add(heading);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        var exit = new Forms.ToolStripMenuItem(hebrew ? "סיום ליווי ויציאה" : "End assistance and exit");
        exit.Click += (_, _) =>
        {
            var answer = System.Windows.MessageBox.Show(
                hebrew ? "האם לסיים את הליווי? היישום המודרך יישאר פתוח." : "End assistance? The target application will remain open.",
                "GuideMe",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes)
            {
                exit.Enabled = false;
                stop();
            }
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
    }

    private static Icon ResolveIcon()
    {
        // Use the exact artwork shipped with the Chrome extension, embedded
        // at build time so customer installations need no external image file.
        using (var stream = typeof(GuideSessionTray).Assembly.GetManifestResourceStream("GuideMe.TrayIcon.png"))
        {
            if (stream is not null)
            {
                using var bitmap = new Bitmap(stream);
                var handle = bitmap.GetHicon();
                try
                {
                    using var temporary = Icon.FromHandle(handle);
                    return (Icon)temporary.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

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
