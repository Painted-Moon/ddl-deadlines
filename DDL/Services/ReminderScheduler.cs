using DDL.Models;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

namespace DDL.Services;

/// Scheduled Windows toasts, one per reminder of each task. Windows delivers them even when DDL is closed.
/// Tags are "<task id>-<minutes>", so everything for a task can be found by its id prefix.
/// <param name="group">Toast group this instance owns ("ddl", or "ddl-test" for test runs).</param>
public sealed class ReminderScheduler(string group)
{
    /// Last failure, shown in the main window. Null after a successful call.
    public string? LastError { get; private set; }

    public bool NotificationsEnabled
    {
        get
        {
            try { return ToastNotificationManagerCompat.CreateToastNotifier().Setting == NotificationSetting.Enabled; }
            catch { return false; }
        }
    }

    /// Call after any change to title, due date, reminders, or completion.
    public void Sync(DdlTask task) => Run(() =>
    {
        RemoveFor(task.Id);
        ScheduleIfNeeded(task);
    });

    public void Cancel(Guid id) => Run(() => RemoveFor(id));

    /// Rebuilds every scheduled reminder from the saved tasks (run at startup).
    public void SyncAll(IEnumerable<DdlTask> tasks) => Run(() =>
    {
        RemoveScheduled(_ => true);
        foreach (var task in tasks) ScheduleIfNeeded(task);
    });

    private void ScheduleIfNeeded(DdlTask task)
    {
        if (task.IsCompleted) return;
        var now = DateTime.Now;
        foreach (var minutes in task.ReminderMinutes.Distinct())
        {
            var fireAt = task.DueDate.AddMinutes(-minutes);
            if (fireAt <= now) continue;   // already past: skip this one

            new ToastContentBuilder()
                .AddArgument("taskId", task.Id.ToString())
                .AddText(task.Title)
                .AddText(minutes == 0
                    ? $"Due now · {Format.Due(task.DueDate)}"
                    : $"Due in {Reminders.Amount(minutes)} · {Format.Due(task.DueDate)}")
                .Schedule(new DateTimeOffset(fireAt), toast =>
                {
                    toast.Tag = $"{TagPrefix(task.Id)}-{minutes}";
                    toast.Group = group;
                });
        }
    }

    /// Removes a task's scheduled reminders and any of its reminders still in the notification list.
    private void RemoveFor(Guid id)
    {
        var prefix = TagPrefix(id);
        RemoveScheduled(tag => tag.StartsWith(prefix, StringComparison.Ordinal));
        try
        {
            foreach (var shown in ToastNotificationManagerCompat.History.GetHistory())
            {
                if (shown.Group == group && shown.Tag.StartsWith(prefix, StringComparison.Ordinal))
                    ToastNotificationManagerCompat.History.Remove(shown.Tag, group);
            }
        }
        catch
        {
            // clearing old notifications is a nicety; scheduling is what matters
        }
    }

    /// Removes this group's scheduled toasts whose tag matches.
    private void RemoveScheduled(Func<string, bool> tagMatches)
    {
        var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
        foreach (var toast in notifier.GetScheduledToastNotifications())
        {
            if (toast.Group == group && tagMatches(toast.Tag ?? ""))
                notifier.RemoveFromSchedule(toast);
        }
    }

    // DDL 0.1 used the bare id as the tag; it still matches this prefix.
    private static string TagPrefix(Guid id) => id.ToString("N");

    private void Run(Action action)
    {
        try
        {
            action();
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }
}
