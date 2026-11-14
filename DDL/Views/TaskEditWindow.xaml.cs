using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DDL.Models;

namespace DDL.Views;

/// What the editor hands back on Save.
public sealed record TaskDraft(string Title, string Notes, DateTime DueDate, IReadOnlyList<int> ReminderMinutes)
{
    public void ApplyTo(DdlTask task)
    {
        task.Title = Title;
        task.Notes = Notes;
        task.DueDate = DueDate;
        task.ReminderMinutes = [.. ReminderMinutes];
    }
}

/// Create (task == null) or edit a task. Does not save anything itself.
public partial class TaskEditWindow : Window
{
    private static readonly string[] TimeFormats = ["H:mm", "HH:mm"];
    private readonly string _originalTitle;
    private readonly SortedSet<int> _selected;     // chosen reminder offsets (minutes before)
    private readonly SortedSet<int> _shown;        // offsets that have a pill: presets + custom ones
    private ToggleButton? _customToggle;

    /// <param name="day">For a new task: the day it is due (from the calendar). Defaults to tomorrow.</param>
    public TaskEditWindow(DdlTask? task, DateTime? day = null)
    {
        InitializeComponent();

        var due = task?.DueDate ?? DefaultDue(day);
        _originalTitle = task?.Title ?? "";
        _selected = [.. (IEnumerable<int>?)task?.ReminderMinutes ?? Reminders.Default];
        _shown = [.. Reminders.Presets, .. _selected];

        HeaderText.Text = task is null ? "New deadline" : "Edit deadline";
        TitleBox.Text = task?.Title ?? "";
        NotesBox.Text = task?.Notes ?? "";
        DuePicker.SelectedDate = due.Date;
        TimeBox.ItemsSource = Enumerable.Range(0, 48).Select(i => TimeSpan.FromMinutes(30 * i).ToString(@"hh\:mm")).ToList();
        TimeBox.Text = due.ToString("HH:mm", CultureInfo.InvariantCulture);
        BuildReminderChips();
        DeleteDeadlineButton.Visibility = task is null ? Visibility.Collapsed : Visibility.Visible;

        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
        Motion.PopInOnLoad(this);
        Validate();
    }

    public TaskDraft? Draft { get; private set; }
    public bool DeleteRequested { get; private set; }

    /// 18:00 on the given day (tomorrow if none); 23:59 if that time already passed today.
    private static DateTime DefaultDue(DateTime? day)
    {
        var due = (day ?? DateTime.Today.AddDays(1)).Date.AddHours(18);
        if (due <= DateTime.Now) due = due.Date.AddHours(23).AddMinutes(59);
        return DateTime.SpecifyKind(due, DateTimeKind.Local);
    }

    // ---- Reminder pills ----

    private void BuildReminderChips()
    {
        var chipStyle = (Style)FindResource("ChipToggle");
        ReminderChips.Children.Clear();
        foreach (var minutes in _shown)
        {
            var chip = new ToggleButton
            {
                Content = Reminders.ChipLabel(minutes),
                Tag = minutes,
                Style = chipStyle,
                IsChecked = _selected.Contains(minutes),
            };
            AutomationProperties.SetName(chip, Reminders.Sentence(minutes));
            chip.Checked += OnChipToggled;
            chip.Unchecked += OnChipToggled;
            ReminderChips.Children.Add(chip);
        }

        _customToggle = new ToggleButton { Content = "+ Custom", Style = chipStyle, IsChecked = CustomPanel.IsVisible };
        AutomationProperties.SetName(_customToggle, "Custom reminder");
        _customToggle.Checked += (_, _) =>
        {
            CustomPanel.Visibility = Visibility.Visible;
            Motion.Enter(CustomPanel, dy: -6, durationMs: 160);
            CustomAmountBox.Focus();
        };
        _customToggle.Unchecked += (_, _) => CustomPanel.Visibility = Visibility.Collapsed;
        ReminderChips.Children.Add(_customToggle);
        Validate();
    }

    private void OnChipToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: int minutes } chip) return;
        if (chip.IsChecked == true) _selected.Add(minutes);
        else _selected.Remove(minutes);
        Validate();
    }

    private void OnCustomAmountChanged(object sender, TextChangedEventArgs e) =>
        AddCustomButton.IsEnabled = TryGetCustomMinutes(out _) is null;

    private void OnCustomAmountKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;   // Enter adds the reminder instead of saving the whole deadline
        AddCustom();
    }

    private void OnAddCustomClick(object sender, RoutedEventArgs e) => AddCustom();

    private void AddCustom()
    {
        if (TryGetCustomMinutes(out var minutes) is { } problem)
        {
            ShowHint(problem);
            return;
        }
        if (!_selected.Contains(minutes) && _selected.Count >= Reminders.MaxCount)
        {
            ShowHint($"A deadline can have up to {Reminders.MaxCount} reminders.");
            return;
        }
        _shown.Add(minutes);
        _selected.Add(minutes);
        CustomAmountBox.Clear();
        CustomPanel.Visibility = Visibility.Collapsed;
        BuildReminderChips();
        if (ReminderChips.Children.OfType<ToggleButton>().FirstOrDefault(c => c.Tag is int m && m == minutes) is { } added)
            Motion.Pop(added, durationMs: 220);
    }

    /// Returns a problem to show, or null when the custom amount is usable.
    private string? TryGetCustomMinutes(out int minutes)
    {
        minutes = 0;
        if (!int.TryParse(CustomAmountBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return "Enter a whole number, like 3.";
        var unit = (CustomUnitBox.SelectedItem as ComboBoxItem)?.Tag is string tag ? int.Parse(tag, CultureInfo.InvariantCulture) : 60;
        var total = (long)amount * unit;
        if (total > Reminders.MaxMinutes) return "Reminders can be at most 60 days before the deadline.";
        minutes = (int)total;
        return null;
    }

    // ---- Validation and saving ----

    private void OnInputChanged(object sender, RoutedEventArgs e) => Validate();

    private bool TryGetDue(out DateTime due)
    {
        due = default;
        if (DuePicker.SelectedDate is not DateTime date) return false;
        if (!TimeOnly.TryParseExact(TimeBox.Text.Trim(), TimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time)) return false;
        // Always local time, so every due date is saved with its UTC offset.
        due = DateTime.SpecifyKind(date.Date + time.ToTimeSpan(), DateTimeKind.Local);
        return true;
    }

    private void Validate()
    {
        if (SaveButton is null || HintBox is null || ReminderSummary is null || _selected is null) return;   // still loading

        ReminderSummary.Text = Reminders.Describe(_selected);

        var hasDue = TryGetDue(out var due);
        SaveButton.IsEnabled = hasDue && !string.IsNullOrWhiteSpace(TitleBox.Text);

        var now = DateTime.Now;
        var past = hasDue ? _selected.Count(m => due.AddMinutes(-m) <= now) : 0;   // no date yet: nothing to compare
        ShowHint(!hasDue ? "Pick a date and a time like 18:00."
            : past == 0 ? null
            : past == _selected.Count ? "These reminder times have already passed, so no notification will be sent."
            : "Reminders whose time has already passed will be skipped.");
    }

    private void ShowHint(string? hint)
    {
        HintText.Text = hint ?? "";
        HintBox.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetDue(out var due) || string.IsNullOrWhiteSpace(TitleBox.Text)) return;
        Draft = new TaskDraft(TitleBox.Text.Trim(), NotesBox.Text.Trim(), due, [.. _selected]);
        DialogResult = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.ConfirmDelete(this, _originalTitle)) return;
        DeleteRequested = true;
        DialogResult = true;
    }
}
