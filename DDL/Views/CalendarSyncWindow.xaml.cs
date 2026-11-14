using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using DDL.Models;
using DDL.Services;

namespace DDL.Views;

/// Turns Outlook sync on or off and walks the user through the one-time GitHub setup.
public partial class CalendarSyncWindow : Window
{
    // Fetches the way Outlook's servers do: no cookies kept between redirects.
    private static readonly HttpClient ServerStyleHttp =
        new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(15) };

    private readonly AppSettings _settings;
    private readonly CalendarFeed _feed;
    private readonly Func<IReadOnlyList<DdlTask>> _tasks;
    private readonly bool _loading;
    private CancellationTokenSource? _work;
    private string? _checkedUrl;

    public CalendarSyncWindow(AppSettings settings, CalendarFeed feed, Func<IReadOnlyList<DdlTask>> tasks)
    {
        _loading = true;
        InitializeComponent();
        _settings = settings;
        _feed = feed;
        _tasks = tasks;

        EnabledBox.IsChecked = settings.CalendarFeedEnabled;
        CompletedBox.IsChecked = settings.CalendarFeedIncludeCompleted;
        _loading = false;

        _feed.UploadFinished += OnUploadFinished;
        Motion.PopInOnLoad(this);
        UpdateView();
    }

    private void OnUploadFinished() => Dispatcher.InvokeAsync(UpdateView);

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.CalendarFeedEnabled = EnabledBox.IsChecked == true;
        _settings.CalendarFeedIncludeCompleted = CompletedBox.IsChecked == true;
        _settings.Save();
        _feed.Publish(_tasks());
        UpdateView();
    }

    private void OnShowFolderClick(object sender, RoutedEventArgs e)
    {
        var path = _feed.FilePath;
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    private void OnOpenGitHubClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(GistPublisher.NewTokenUrl) { UseShellExecute = true });

    private void OnTokenChanged(object sender, RoutedEventArgs e) =>
        ConnectButton.IsEnabled = TokenBox.Password.Trim().Length > 0;

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password.Trim();
        if (token.Length == 0) return;

        _work?.Cancel();
        var cancel = _work = new CancellationTokenSource();
        ConnectButton.IsEnabled = false;
        TokenBox.IsEnabled = false;
        Show(ConnectText, "Connecting to GitHub and creating the secret gist…", "TextFillColorSecondaryBrush");
        try
        {
            await _feed.ConnectAsync(token, _tasks(), cancel.Token);
            TokenBox.Clear();
            ConnectText.Visibility = Visibility.Collapsed;
            EnabledBox.IsChecked = true;   // ConnectAsync turned sync on
        }
        catch (GistException ex)
        {
            Show(ConnectText, ex.Message, "SystemFillColorCriticalBrush");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Show(ConnectText, $"Couldn't connect: {ex.Message}", "SystemFillColorCriticalBrush");
        }
        finally
        {
            TokenBox.IsEnabled = true;
            ConnectButton.IsEnabled = TokenBox.Password.Trim().Length > 0;
            UpdateView();
        }
    }

    private void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Stop updating the calendar and forget the GitHub token?\n\n" +
            "Outlook keeps showing the last version until you remove the DDL calendar there. " +
            "The gist stays on GitHub until you delete it at gist.github.com.",
            "Disconnect GitHub", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _feed.Disconnect();
        _checkedUrl = null;
        UpdateView();
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SubscribeBox.Text);
            CopyButton.Content = "Copied";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopyButton.Content = "Try again";   // another app is holding the clipboard
        }
    }

    private void UpdateView()
    {
        var on = _settings.CalendarFeedEnabled;
        string status;
        string brush = "TextFillColorPrimaryBrush";
        if (!on)
        {
            status = "Off: the calendar isn't being updated.";
            brush = "TextFillColorSecondaryBrush";
        }
        else if (_feed.LastUploadError is { } uploadError)
        {
            status = uploadError;
            brush = "SystemFillColorCriticalBrush";
        }
        else if (_feed.LastError is { } fileError)
        {
            status = $"Couldn't save the local copy: {fileError}";
            brush = "SystemFillColorCriticalBrush";
        }
        else
        {
            var deadlines = $"{_feed.LastCount} deadline{(_feed.LastCount == 1 ? "" : "s")}";
            status = !_feed.IsConnected ? $"{deadlines} · not connected to Outlook yet"
                : _feed.IsUploading ? $"{deadlines} · uploading…"
                : _feed.LastUploaded is { } at ? $"{deadlines} · uploaded {at:HH:mm}"
                : $"{deadlines} · up to date";
        }
        StatusText.Text = status;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        PathText.Text = $"Local copy: {_feed.FilePath}";
        PathText.ToolTip = _feed.FilePath;

        var connected = _feed.IsConnected;
        SetupPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        ConnectedPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        if (!connected) return;

        ConnectedText.Text = $"✓ Connected to GitHub as {_feed.Owner}.";
        var url = _feed.SubscribeUrl!;
        if (SubscribeBox.Text != url)
        {
            SubscribeBox.Text = url;
            CopyButton.Content = "Copy";
        }
        if (_checkedUrl != url)
        {
            _checkedUrl = url;
            CheckAddress(url);
        }
    }

    /// Fetches the address the way Outlook's servers will, and says whether it returns the calendar.
    private async void CheckAddress(string url)
    {
        var cancel = new CancellationTokenSource();
        Show(CheckText, "Checking that Outlook will be able to read this address…", "TextFillColorSecondaryBrush");
        try
        {
            using var response = await ServerStyleHttp.GetAsync(url, cancel.Token);
            var body = await response.Content.ReadAsStringAsync(cancel.Token);
            if (response.IsSuccessStatusCode && body.StartsWith("BEGIN:VCALENDAR", StringComparison.Ordinal))
            {
                var count = System.Text.RegularExpressions.Regex.Matches(body, "BEGIN:VEVENT").Count;
                Show(CheckText, $"✓ Works: the address returns your calendar ({count} deadline{(count == 1 ? "" : "s")}).",
                    "SystemFillColorSuccessBrush");
            }
            else
            {
                Show(CheckText, $"The address didn't return the calendar (error {(int)response.StatusCode}). " +
                                "Try Disconnect and connect again.", "SystemFillColorCriticalBrush");
            }
        }
        catch (Exception ex)
        {
            Show(CheckText, $"Couldn't check the address ({ex.Message}). It may still work in Outlook.",
                "SystemFillColorCautionBrush");
        }
    }

    private static void Show(TextBlock target, string text, string brushKey)
    {
        target.Text = text;
        target.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        target.Visibility = Visibility.Visible;
    }

    protected override void OnClosed(EventArgs e)
    {
        _feed.UploadFinished -= OnUploadFinished;
        _work?.Cancel();
        base.OnClosed(e);
    }
}
