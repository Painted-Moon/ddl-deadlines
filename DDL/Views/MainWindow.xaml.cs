using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DDL.Models;
using DDL.Services;
using DDL.ViewModels;

namespace DDL.Views;

public partial class MainWindow : Window
{
    private static readonly (Bucket Bucket, string Title)[] OpenBuckets =
    [
        (Bucket.Overdue, "Overdue"),
        (Bucket.Today, "Today"),
        (Bucket.Tomorrow, "Tomorrow"),
        (Bucket.NextWeek, "Next 7 days"),
        (Bucket.Later, "Later"),
    ];

    private readonly TaskStore _store;
    private readonly ReminderScheduler _reminders;
    private readonly AppSettings _settings;
    private readonly CalendarFeed _feed;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private List<TaskRow> _rows = [];
    private string _layoutKey = "";
    private TaskSection? _completedSection;
    private bool _completedExpanded;

    public MainWindow(TaskStore store, ReminderScheduler reminders, AppSettings settings, CalendarFeed feed)
    {
        InitializeComponent();
        _store = store;
        _reminders = reminders;
        _settings = settings;
        _feed = feed;

        Calendar.SelectionChanged += (_, _) => RefreshList();
        Calendar.DayActivated += (_, day) => CreateTask(day);

        _clock.Tick += OnClockTick;
        _clock.Start();
        Activated += (_, _) => UpdateNotificationWarning();
        _feed.UploadFinished += () => Dispatcher.InvokeAsync(UpdateNotificationWarning);
        UpdateThemeButton();
        Refresh();
    }

    /// Raised with (overdue, due today) whenever the counts may have changed (drives the tray icon).
    public event Action<int, int>? SummaryChanged;

    public void RefreshSummary() => UpdateCounts(DateTime.Now);

    /// Opens the editor for a new deadline (from the tray menu), unless a dialog is already open.
    public void NewDeadline()
    {
        if (OwnedWindows.Count == 0) CreateTask(Calendar.SelectedDate);
    }

    /// Brings the window forward (also out of the tray) and, if given, opens that task.
    public void ShowTask(Guid? id)
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (id is { } taskId && OwnedWindows.Count == 0 && _store.Find(taskId) is { } task)
            OpenEditor(task);
    }

    /// Full refresh after tasks changed: calendar dots, counts and list.
    private void Refresh()
    {
        Calendar.SetTasks(_store.Tasks);
        RefreshList();
    }

    private void RefreshList()
    {
        var now = DateTime.Now;
        if (_completedSection is not null) _completedExpanded = _completedSection.IsExpanded;

        TodayWeekday.Text = now.ToString("dddd", Format.English).ToUpperInvariant();
        TodayDate.Text = now.ToString("MMMM d", Format.English);
        UpdateCounts(now);

        var day = Calendar.SelectedDate;
        var visible = day is { } d ? _store.Tasks.Where(t => t.DueDate.Date == d).ToList() : _store.Tasks.ToList();

        var sections = new List<TaskSection>();
        if (day is null)
        {
            foreach (var (bucket, title) in OpenBuckets)
            {
                var rows = Rows(visible.Where(t => TaskRow.BucketOf(t, now) == bucket).OrderBy(t => t.DueDate), now);
                if (rows.Count > 0) sections.Add(new TaskSection(title, rows));
            }
        }
        else
        {
            var rows = Rows(visible.Where(t => !t.IsCompleted).OrderBy(t => t.DueDate), now);
            if (rows.Count > 0) sections.Add(new TaskSection("Due this day", rows));
        }

        var done = Rows(visible.Where(t => t.IsCompleted).OrderByDescending(t => t.CompletedAt ?? t.DueDate), now);
        _completedSection = done.Count > 0
            ? new TaskSection("Completed", done, isCollapsible: true, isExpanded: day is not null || _completedExpanded)
            : null;
        if (_completedSection is not null) sections.Add(_completedSection);

        Sections.ItemsSource = sections;
        _rows = sections.SelectMany(s => s.Rows).ToList();
        _layoutKey = LayoutKey(now);

        // Header
        var open = visible.Count(t => !t.IsCompleted);
        var overdue = visible.Count(t => !t.IsCompleted && t.DueDate < now);
        ListTitle.Text = day is { } selected ? selected.ToString("dddd, MMMM d", Format.English) : "Deadlines";
        ListSubtitle.Text = open == 0 ? "Nothing open" : $"{open} open" + (overdue > 0 ? $" · {overdue} overdue" : "");
        ShowAllButton.Visibility = day is null ? Visibility.Collapsed : Visibility.Visible;

        // Empty and all-done states
        EmptyState.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = day is { } empty ? $"Nothing due on {empty.ToString("MMM d", Format.English)}" : "No deadlines yet";
        EmptyText.Text = day is null ? "Click New deadline or press Ctrl+N to add one." : "Double-click a day in the calendar to add one there.";
        EmptyAddButton.Visibility = day is null ? Visibility.Collapsed : Visibility.Visible;
        AllClear.Visibility = visible.Count > 0 && open == 0 ? Visibility.Visible : Visibility.Collapsed;

        UpdateNotificationWarning();
    }

    private static List<TaskRow> Rows(IEnumerable<DdlTask> tasks, DateTime now) =>
        tasks.Select(t => new TaskRow(t, now)).ToList();

    private void UpdateCounts(DateTime now)
    {
        var open = _store.Tasks.Where(t => !t.IsCompleted).ToList();
        var overdue = open.Count(t => t.DueDate < now);
        OverdueCount.Text = overdue.ToString();
        OverdueCount.SetResourceReference(TextBlock.ForegroundProperty,
            overdue > 0 ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush");
        var dueToday = open.Count(t => t.DueDate >= now && t.DueDate.Date == now.Date);
        TodayCount.Text = dueToday.ToString();
        WeekCount.Text = open.Count(t => t.DueDate >= now && t.DueDate < now.Date.AddDays(7)).ToString();
        SummaryChanged?.Invoke(overdue, dueToday);
    }

    /// Changes when a task moves to another group or the day changes; then the list is rebuilt.
    private string LayoutKey(DateTime now) =>
        now.Date.ToString("yyyyMMdd") + string.Concat(_store.Tasks.Select(t => (int)TaskRow.BucketOf(t, now)));

    private void OnClockTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        if (LayoutKey(now) != _layoutKey)
        {
            Refresh();
            return;
        }
        foreach (var row in _rows) row.UpdateClock(now);
        UpdateCounts(now);
    }

    private void UpdateNotificationWarning()
    {
        string? message = _reminders.LastError is { } error
            ? $"Reminders could not be updated: {error}"
            : !_reminders.NotificationsEnabled
                ? "Windows notifications are off for DDL, so reminders won't show. " +
                  "Turn them on in Settings > System > Notifications."
                : _settings.CalendarFeedEnabled && _feed.LastUploadError is { } uploadError
                    ? $"Outlook sync: {uploadError}"
                    : _settings.CalendarFeedEnabled && _feed.LastError is { } feedError
                        ? $"The calendar file could not be saved: {feedError}"
                        : null;
        NotificationWarningText.Text = message ?? "";
        NotificationWarning.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;

        var (syncText, syncBrush) =
            !_settings.CalendarFeedEnabled ? ("Off", "TextFillColorSecondaryBrush")
            : _feed.LastUploadError is not null || _feed.LastError is not null ? ("Error", "SystemFillColorCriticalBrush")
            : !_feed.IsConnected ? ("Set up", "SystemFillColorCautionBrush")
            : ("On", "SystemFillColorSuccessBrush");
        SyncStatus.Text = syncText;
        SyncStatus.SetResourceReference(TextBlock.ForegroundProperty, syncBrush);
        UpdateStartupStatus();
    }

    private static readonly (string Mode, string Label, string Glyph)[] ThemeChoices =
    [
        ("Light", "Light", ""),
        ("Dark", "Dark", ""),
        ("System", "Use Windows setting", ""),
    ];

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ThemeButton, Placement = PlacementMode.Bottom };
        foreach (var (mode, label, glyph) in ThemeChoices)
        {
            var item = new MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = _settings.Theme == mode,
                Icon = new TextBlock { Text = glyph, FontSize = 14, FontFamily = (FontFamily)FindResource("IconFont") },
            };
            item.Click += (_, _) => SetTheme(mode);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void SetTheme(string mode)
    {
        _settings.Theme = mode;
        _settings.Save();
        App.ApplyTheme(mode);
        UpdateThemeButton();
    }

    private void UpdateThemeButton()
    {
        var choice = ThemeChoices.FirstOrDefault(c => c.Mode == _settings.Theme, ThemeChoices[^1]);
        ThemeGlyph.Text = choice.Glyph;
        ThemeButton.ToolTip = $"Theme: {choice.Label}";
    }

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (AutoStart.IsEnabled) AutoStart.Disable();
            else AutoStart.Enable();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't change Start with Windows: {ex.Message}", "DDL",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        UpdateStartupStatus();
    }

    private void UpdateStartupStatus()
    {
        var on = AutoStart.IsEnabled;
        StartupStatus.Text = on ? "On" : "Off";
        StartupStatus.SetResourceReference(TextBlock.ForegroundProperty,
            on ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush");
    }

    private void OnNewExecuted(object sender, ExecutedRoutedEventArgs e) => CreateTask(Calendar.SelectedDate);
    private void OnEmptyAddClick(object sender, RoutedEventArgs e) => CreateTask(Calendar.SelectedDate);
    private void OnShowAllClick(object sender, RoutedEventArgs e) => Calendar.Select(null);

    private void OnCalendarSyncClick(object sender, RoutedEventArgs e)
    {
        new CalendarSyncWindow(_settings, _feed, () => _store.Tasks) { Owner = this }.ShowDialog();
        UpdateNotificationWarning();
    }

    private void CreateTask(DateTime? day)
    {
        var editor = new TaskEditWindow(null, day) { Owner = this };
        if (editor.ShowDialog() != true || editor.Draft is not { } draft) return;

        var task = new DdlTask();
        draft.ApplyTo(task);
        _store.Add(task);
        _reminders.Sync(task);
        Refresh();
    }

    private void OpenEditor(DdlTask task)
    {
        var editor = new TaskEditWindow(task) { Owner = this };
        if (editor.ShowDialog() != true) return;

        if (editor.DeleteRequested)
        {
            Delete(task);
            return;
        }
        if (editor.Draft is not { } draft) return;

        draft.ApplyTo(task);
        _store.Save();
        _reminders.Sync(task);
        Refresh();
    }

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        // Ignore clicks that bubbled up from the check box or delete button inside the card.
        if (e.OriginalSource != sender) return;
        e.Handled = true;
        if (RowOf(sender) is { } row) OpenEditor(row.Model);
    }

    private void OnCheckChanged(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not CheckBox box || RowOf(sender) is not { } row) return;

        var isChecked = box.IsChecked == true;
        if (isChecked == row.Model.IsCompleted) return;   // set by data binding, not by the user
        SetCompleted(row.Model, isChecked);
    }

    private void OnToggleMenuClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (RowOf(sender) is { } row) SetCompleted(row.Model, !row.Model.IsCompleted);
    }

    private void SetCompleted(DdlTask task, bool completed)
    {
        task.IsCompleted = completed;
        task.CompletedAt = completed ? DateTime.Now : null;
        _store.Save();
        _reminders.Sync(task);
        Refresh();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (RowOf(sender) is { } row && Dialogs.ConfirmDelete(this, row.Title))
            Delete(row.Model);
    }

    private void Delete(DdlTask task)
    {
        _reminders.Cancel(task.Id);
        _store.Delete(task.Id);
        Refresh();
    }

    private static TaskRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as TaskRow;
}
