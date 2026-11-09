using System.ComponentModel;
using DDL.Models;

namespace DDL.ViewModels;

/// How pressing a deadline is. Drives the pill and calendar-dot colors.
public enum Urgency { Overdue, Soon, Upcoming, Done }

/// Where a task is listed when no calendar day is selected.
public enum Bucket { Overdue, Today, Tomorrow, NextWeek, Later, Completed }

/// What one task card shows. UpdateClock refreshes the time-dependent text.
public sealed class TaskRow : INotifyPropertyChanged
{
    public TaskRow(DdlTask model, DateTime now)
    {
        Model = model;
        UpdateClock(now);
    }

    public DdlTask Model { get; }

    public string Title => Model.Title;
    public bool IsCompleted => Model.IsCompleted;
    public bool HasReminder => !Model.IsCompleted && Model.Reminder != ReminderOption.Off;
    public string ReminderText => Model.Reminder.Title();
    public bool HasNotes => !string.IsNullOrWhiteSpace(Model.Notes);
    public string NotesPreview => Model.Notes.Split('\n', 2)[0].Trim();
    public string ToggleLabel => Model.IsCompleted ? "Mark as not done" : "Mark as done";
    public string ToggleAutomationName => $"{ToggleLabel}: {Model.Title}";
    public string DeleteAutomationName => $"Delete {Model.Title}";

    public Urgency Urgency { get; private set; }
    public string DueText { get; private set; } = "";
    public string PillText { get; private set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public static Urgency UrgencyOf(DdlTask task, DateTime now) =>
        task.IsCompleted ? Urgency.Done
        : task.DueDate < now ? Urgency.Overdue
        : task.DueDate - now < TimeSpan.FromHours(24) ? Urgency.Soon
        : Urgency.Upcoming;

    public static Bucket BucketOf(DdlTask task, DateTime now)
    {
        if (task.IsCompleted) return Bucket.Completed;
        if (task.DueDate < now) return Bucket.Overdue;
        return (task.DueDate.Date - now.Date).Days switch
        {
            0 => Bucket.Today,
            1 => Bucket.Tomorrow,
            < 7 => Bucket.NextWeek,
            _ => Bucket.Later,
        };
    }

    public void UpdateClock(DateTime now)
    {
        Urgency = UrgencyOf(Model, now);
        DueText = Format.DueNear(Model.DueDate, now);
        PillText = Model.IsCompleted ? "Done" : Format.Relative(Model.DueDate, now);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));   // all properties
    }
}
