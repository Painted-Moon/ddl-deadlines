using DDL.Models;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

namespace DDL.Services;

/// One scheduled Windows toast per task. Windows delivers it even when DDL is closed.
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

    /// Call after any change to title, due date, reminder, or completion.
    public void Sync(DdlTask task) => Run(() =>
    {
        RemoveScheduled(TagFor(task.Id));
        ToastNotificationManagerCompat.History.Remove(TagFor(task.Id), group);
        ScheduleIfNeeded(task);
    });

    public void Cancel(Guid id) => Run(() =>
    {
        RemoveScheduled(TagFor(id));
        ToastNotificationManagerCompat.History.Remove(TagFor(id), group);
    });

    /// Rebuilds every scheduled reminder from the saved tasks (run at startup).
    public void SyncAll(IEnumerable<DdlTask> tasks) => Run(() =>
    {
        RemoveScheduled(tag: null);
        foreach (var task in tasks) ScheduleIfNeeded(task);
    });

    private void ScheduleIfNeeded(DdlTask task)
    {
        if (task.IsCompleted || task.ReminderDate is not DateTime fireAt || fireAt <= DateTime.Now) return;

        new ToastContentBuilder()
            .AddArgument("taskId", task.Id.ToString())
            .AddText(task.Title)
            .AddText($"Due {Format.Due(task.DueDate)}")
            .Schedule(new DateTimeOffset(fireAt), toast =>
            {
                toast.Tag = TagFor(task.Id);
                toast.Group = group;
            });
    }

    /// Removes this group's scheduled toasts with the given tag, or all of them when tag is null.
    private void RemoveScheduled(string? tag)
    {
        var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
        foreach (var toast in notifier.GetScheduledToastNotifications())
        {
            if (toast.Group == group && (tag is null || toast.Tag == tag))
                notifier.RemoveFromSchedule(toast);
        }
    }

    private static string TagFor(Guid id) => id.ToString("N");

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
