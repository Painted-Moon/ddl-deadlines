using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DDL.Models;
using DDL.Services;
using DDL.Views;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DDL;

/// Command line:
///   (none)    open DDL
///   --tray    start quietly in the notification area (used by Start with Windows)
///   --new     open DDL with a new deadline (taskbar right-click menu)
///   --quit    ask a running DDL to quit (taskbar menu, and before installing a new version)
public partial class App : Application
{
    // Only one DDL may run: two copies would each keep their own task list and overwrite each other's saves.
    private const string InstanceKey = "DDL-6f1c9e2a";
    private Mutex? _singleInstance;            // held for the app's lifetime
    private EventWaitHandle? _showRequest;     // set by a second launch to bring this window forward
    private EventWaitHandle? _quitRequest;     // set by "DDL.exe --quit"
    private EventWaitHandle? _newRequest;      // set by "DDL.exe --new"
    private CalendarFeed? _feed;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        var startInTray = e.Args.Contains("--tray");
        var quitRunning = e.Args.Contains("--quit");
        var newDeadline = e.Args.Contains("--new");

        // Test runs (custom data folder) get their own instance so they never block the real app.
        var key = TaskStore.IsCustomLocation ? InstanceKey + "-test" : InstanceKey;
        _singleInstance = new Mutex(initiallyOwned: true, $@"Local\{key}", out var isFirst);
        if (!isFirst)
        {
            if (quitRunning) Signal($@"Local\{key}-quit");
            else if (!startInTray)
            {
                AllowSetForegroundWindow(AsfwAny);   // let the running copy take the foreground
                Signal($@"Local\{key}-{(newDeadline ? "new" : "show")}");
            }
            Shutdown();
            return;
        }
        if (quitRunning)   // nothing running to quit
        {
            Shutdown();
            return;
        }
        _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{key}-show");
        _quitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{key}-quit");
        _newRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{key}-new");

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/DDL;component/Views/Theme.xaml", UriKind.Relative),
        });

        var store = new TaskStore(TaskStore.DefaultPath);
        // Test runs use their own reminder group so they never touch real reminders.
        var reminders = new ReminderScheduler(TaskStore.IsCustomLocation ? "ddl-test" : "ddl");
        reminders.SyncAll(store.Tasks);

        var settings = AppSettings.Load(store.FolderPath);
        // DDL_THEME=Light|Dark overrides the saved choice (handy for checking both looks).
        ApplyTheme(Environment.GetEnvironmentVariable("DDL_THEME") is { Length: > 0 } forced ? forced : settings.Theme);
        var feed = _feed = new CalendarFeed(settings, CalendarFeed.DefaultPathFor(TaskStore.IsCustomLocation, store.FolderPath));
        store.Saved += () => feed.Publish(store.Tasks);
        feed.Publish(store.Tasks);

        var window = _window = new MainWindow(store, reminders, settings, feed);
        MainWindow = window;
        // Closing the window keeps DDL in the tray; Quit in the tray menu really exits.
        window.Closing += (_, args) =>
        {
            if (_quitting) return;
            args.Cancel = true;
            window.Hide();
            ShowTrayHintOnce(settings);
        };
        SessionEnding += (_, _) => _quitting = true;   // let Windows sign-out close us

        _tray = new TrayIcon();
        _tray.OpenRequested += () => window.ShowTask(null);
        _tray.NewDeadlineRequested += () =>
        {
            window.ShowTask(null);
            window.NewDeadline();
        };
        _tray.QuitRequested += Quit;
        window.SummaryChanged += (overdue, dueToday) => _tray?.Update(overdue, dueToday);
        window.RefreshSummary();

        if (!startInTray) window.Show();
        if (newDeadline) Dispatcher.InvokeAsync(window.NewDeadline, DispatcherPriority.ApplicationIdle);
        SetUpJumpList();

        ThreadPool.RegisterWaitForSingleObject(_showRequest,
            (_, _) => Dispatcher.InvokeAsync(() => window.ShowTask(null)), null, Timeout.Infinite, executeOnlyOnce: false);
        ThreadPool.RegisterWaitForSingleObject(_newRequest,
            (_, _) => Dispatcher.InvokeAsync(() =>
            {
                window.ShowTask(null);
                window.NewDeadline();
            }), null, Timeout.Infinite, executeOnlyOnce: false);
        ThreadPool.RegisterWaitForSingleObject(_quitRequest,
            (_, _) => Dispatcher.InvokeAsync(Quit), null, Timeout.Infinite, executeOnlyOnce: true);

        // Raised when the user clicks a reminder, including when the click launched the app.
        // It arrives on a background thread.
        ToastNotificationManagerCompat.OnActivated += toast =>
        {
            var args = ToastArguments.Parse(toast.Argument);
            Guid? taskId = args.TryGetValue("taskId", out var raw) && Guid.TryParse(raw, out var id) ? id : null;
            Dispatcher.InvokeAsync(() => window.ShowTask(taskId));
        };
    }

    private void Quit()
    {
        _quitting = true;
        _tray?.Dispose();
        _tray = null;
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _feed?.WaitForUploads(TimeSpan.FromSeconds(5));   // let the last calendar change reach GitHub
        base.OnExit(e);
    }

    /// The first time the window is closed, explain that DDL is still in the tray.
    private static void ShowTrayHintOnce(AppSettings settings)
    {
        if (settings.TrayHintShown) return;
        settings.TrayHintShown = true;
        settings.Save();
        try
        {
            new ToastContentBuilder()
                .AddText("DDL is still running")
                .AddText("It stays in the notification area (click ^ on the taskbar if it's hidden). Right-click the icon to quit.")
                .Show(toast =>
                {
                    toast.Tag = "tray-hint";
                    toast.Group = "info";
                });
        }
        catch
        {
            // a hint is not worth an error message
        }
    }

    /// Switches the whole app between "Light", "Dark" and "System" (follow Windows) right away.
    public static void ApplyTheme(string? mode) =>
        Current.ThemeMode = mode switch
        {
            "Light" => ThemeMode.Light,
            "Dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

    /// Tasks in the taskbar button's right-click menu. They start DDL.exe with an argument,
    /// which the running copy picks up.
    private static void SetUpJumpList()
    {
        var exe = Environment.ProcessPath!;
        var list = new System.Windows.Shell.JumpList { ShowFrequentCategory = false, ShowRecentCategory = false };
        list.JumpItems.Add(new System.Windows.Shell.JumpTask
        {
            Title = "New deadline",
            Description = "Add a deadline",
            ApplicationPath = exe,
            Arguments = "--new",
            IconResourcePath = exe,
        });
        list.JumpItems.Add(new System.Windows.Shell.JumpTask
        {
            Title = "Quit DDL",
            Description = "Close DDL completely. Reminders still arrive.",
            ApplicationPath = exe,
            Arguments = "--quit",
        });
        System.Windows.Shell.JumpList.SetJumpList(Current, list);
        list.Apply();
    }

    private static void Signal(string name)
    {
        try { EventWaitHandle.OpenExisting(name).Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "DDL", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private const int AsfwAny = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
