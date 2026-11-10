using System.Text.Json.Serialization;

namespace DDL.Models;

public sealed class DdlTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime DueDate { get; set; }                 // local wall-clock time

    /// Minutes before the due time, one reminder each (0 = at the due time). Empty = no reminders.
    public List<int> ReminderMinutes { get; set; } = [.. Reminders.Default];

    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// DDL 0.1 stored a single "Reminder"; read it once and convert (see UpgradeLegacyReminder).
    [JsonPropertyName("Reminder"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyReminder? LegacyReminder { get; set; }

    /// Converts a DDL 0.1 reminder into ReminderMinutes. Returns true if anything changed.
    public bool UpgradeLegacyReminder()
    {
        if (LegacyReminder is not { } old) return false;
        ReminderMinutes = old == Models.LegacyReminder.Off ? [] : [(int)old];
        LegacyReminder = null;
        return true;
    }
}
