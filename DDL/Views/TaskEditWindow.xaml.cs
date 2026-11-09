using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DDL.Models;

namespace DDL.Views;

/// What the editor hands back on Save.
public sealed record TaskDraft(string Title, string Notes, DateTime DueDate, ReminderOption Reminder)
{
    public void ApplyTo(DdlTask task)
    {
        task.Title = Title;
        task.Notes = Notes;
        task.DueDate = DueDate;
        task.Reminder = Reminder;
    }
}

/// Create (task == null) or edit a task. Does not save anything itself.
public partial class TaskEditWindow : Window
{
    private static readonly string[] TimeFormats = ["H:mm", "HH:mm"];
    private readonly string _originalTitle;

    /// <param name="day">For a new task: the day it is due (from the calendar). Defaults to tomorrow.</param>
    public TaskEditWindow(DdlTask? task, DateTime? day = null)
    {
        InitializeComponent();

        var due = task?.DueDate ?? DefaultDue(day);
        _originalTitle = task?.Title ?? "";

        HeaderText.Text = task is null ? "New deadline" : "Edit deadline";
        TitleBox.Text = task?.Title ?? "";
        NotesBox.Text = task?.Notes ?? "";
        DuePicker.SelectedDate = due.Date;
        TimeBox.ItemsSource = Enumerable.Range(0, 48).Select(i => TimeSpan.FromMinutes(30 * i).ToString(@"hh\:mm")).ToList();
        TimeBox.Text = due.ToString("HH:mm", CultureInfo.InvariantCulture);
        BuildReminderChips(task?.Reminder ?? ReminderOption.OneHour);
        DeleteButton.Visibility = task is null ? Visibility.Collapsed : Visibility.Visible;

        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
        Validate();
    }

    public TaskDraft? Draft { get; private set; }
    public bool DeleteRequested { get; private set; }

    private ReminderOption SelectedReminder =>
        ReminderChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag is ReminderOption option
            ? option
            : ReminderOption.Off;

    /// 18:00 on the given day (tomorrow if none); 23:59 if that time already passed today.
    private static DateTime DefaultDue(DateTime? day)
    {
        var due = (day ?? DateTime.Today.AddDays(1)).Date.AddHours(18);
        if (due <= DateTime.Now) due = due.Date.AddHours(23).AddMinutes(59);
        return DateTime.SpecifyKind(due, DateTimeKind.Local);
    }

    private void BuildReminderChips(ReminderOption selected)
    {
        foreach (var choice in ReminderChoice.All)
        {
            var chip = new RadioButton
            {
                Content = choice.Option.ChipTitle(),
                Tag = choice.Option,
                GroupName = "Reminder",
                Style = (Style)FindResource("ChipRadio"),
                IsChecked = choice.Option == selected,
            };
            AutomationProperties.SetName(chip, choice.Title);
            chip.Checked += (_, _) => Validate();
            ReminderChips.Children.Add(chip);
        }
    }

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
        if (SaveButton is null || HintBox is null || ReminderChips is null) return;   // still inside InitializeComponent

        var hasDue = TryGetDue(out var due);
        SaveButton.IsEnabled = hasDue && !string.IsNullOrWhiteSpace(TitleBox.Text);

        string? hint = !hasDue
            ? "Pick a date and a time like 18:00."
            : SelectedReminder.FireDate(due) is DateTime fireAt && fireAt <= DateTime.Now
                ? "This reminder time has already passed, so no notification will be sent."
                : null;
        HintText.Text = hint ?? "";
        HintBox.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!TryGetDue(out var due) || string.IsNullOrWhiteSpace(TitleBox.Text)) return;
        Draft = new TaskDraft(TitleBox.Text.Trim(), NotesBox.Text.Trim(), due, SelectedReminder);
        DialogResult = true;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.ConfirmDelete(this, _originalTitle)) return;
        DeleteRequested = true;
        DialogResult = true;
    }
}
