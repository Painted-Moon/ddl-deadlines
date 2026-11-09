using System.Text.Json.Serialization;

namespace DDL.Models;

public sealed class DdlTask
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime DueDate { get; set; }                 // local wall-clock time
    public ReminderOption Reminder { get; set; } = ReminderOption.OneHour;
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    [JsonIgnore] public DateTime? ReminderDate => Reminder.FireDate(DueDate);
}
