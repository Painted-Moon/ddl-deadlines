using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using DDL.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DDL.Views;

/// DDL's icon in the notification area. Click opens DDL; right-click shows a Windows 11-style menu.
/// A red dot appears on the icon while anything is overdue.
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _normalIcon;
    private readonly Drawing.Icon _alertIcon;
    private readonly IntPtr _alertHandle;
    private readonly ContextMenu _menu;
    private readonly MenuItem _autoStartItem;

    public event Action? OpenRequested;
    public event Action? NewDeadlineRequested;
    public event Action? QuitRequested;

    public TrayIcon()
    {
        var size = Forms.SystemInformation.SmallIconSize;
        using (var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ddl.ico"))!.Stream)
            _normalIcon = new Drawing.Icon(stream, size);
        (_alertIcon, _alertHandle) = WithAlertDot(_normalIcon);

        _autoStartItem = new MenuItem { Header = "Start with Windows", IsCheckable = true };
        _autoStartItem.Click += OnAutoStartClick;

        _menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        _menu.Items.Add(Item("Open DDL", "", () => OpenRequested?.Invoke()));
        _menu.Items.Add(Item("New deadline…", "", () => NewDeadlineRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(_autoStartItem);
        _menu.Items.Add(new Separator());
        _menu.Items.Add(Item("Quit DDL", "", () => QuitRequested?.Invoke()));
        // A tray menu must own the foreground, or it won't close when you click elsewhere.
        _menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(_menu) is HwndSource source) SetForegroundWindow(source.Handle);
        };

        _notifyIcon = new Forms.NotifyIcon { Icon = _normalIcon, Text = "DDL", Visible = true };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke();
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
    }

    /// Updates the tooltip and the overdue dot.
    public void Update(int overdue, int dueToday)
    {
        _notifyIcon.Icon = overdue > 0 ? _alertIcon : _normalIcon;
        var text = overdue > 0 ? $"DDL · {overdue} overdue · {dueToday} due today"
                 : dueToday > 0 ? $"DDL · {dueToday} due today"
                 : "DDL · nothing due today";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void ShowMenu()
    {
        _autoStartItem.IsChecked = AutoStart.IsEnabled;
        _menu.IsOpen = true;
    }

    private void OnAutoStartClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_autoStartItem.IsChecked) AutoStart.Enable();
            else AutoStart.Disable();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't change Start with Windows: {ex.Message}", "DDL",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _autoStartItem.IsChecked = AutoStart.IsEnabled;
    }

    private static MenuItem Item(string header, string glyph, Action action)
    {
        var item = new MenuItem
        {
            Header = header,
            Icon = new TextBlock
            {
                Text = glyph,
                FontSize = 14,
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static (Drawing.Icon Icon, IntPtr Handle) WithAlertDot(Drawing.Icon baseIcon)
    {
        using var bitmap = baseIcon.ToBitmap();
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var d = bitmap.Width * 0.5f;
            var x = bitmap.Width - d - 0.5f;
            var y = bitmap.Height - d - 0.5f;
            using var halo = new Drawing.SolidBrush(Drawing.Color.White);
            using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(232, 17, 35));
            g.FillEllipse(halo, x - 1, y - 1, d + 1.5f, d + 1.5f);
            g.FillEllipse(dot, x, y, d - 0.5f, d - 0.5f);
        }
        var handle = bitmap.GetHicon();
        return (Drawing.Icon.FromHandle(handle), handle);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _normalIcon.Dispose();
        _alertIcon.Dispose();
        DestroyIcon(_alertHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
