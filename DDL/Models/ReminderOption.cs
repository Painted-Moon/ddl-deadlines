namespace DDL.Models;

/// Minutes before the due time. Off = no reminder.
public enum ReminderOption
{
    Off = -1,
    AtDue = 0,
    Minutes15 = 15,
    OneHour = 60,
    OneDay = 1440,
}

public static class ReminderOptionExtensions
{
    public static string Title(this ReminderOption option) => option switch
    {
        ReminderOption.Off => "None",
        ReminderOption.AtDue => "At due time",
        ReminderOption.Minutes15 => "15 min before",
        ReminderOption.OneHour => "1 hour before",
        ReminderOption.OneDay => "1 day before",
        _ => option.ToString(),
    };

    /// Short label for the reminder chips in the editor.
    public static string ChipTitle(this ReminderOption option) => option switch
    {
        ReminderOption.Off => "None",
        ReminderOption.AtDue => "At the time",
        ReminderOption.Minutes15 => "15 min",
        ReminderOption.OneHour => "1 hour",
        ReminderOption.OneDay => "1 day",
        _ => option.ToString(),
    };

    public static DateTime? FireDate(this ReminderOption option, DateTime due) =>
        option == ReminderOption.Off ? null : due.AddMinutes(-(int)option);
}

/// One entry in the reminder drop-down.
public sealed record ReminderChoice(ReminderOption Option, string Title)
{
    public static IReadOnlyList<ReminderChoice> All { get; } =
        Enum.GetValues<ReminderOption>().OrderBy(o => (int)o)   // GetValues puts -1 (Off) last
            .Select(o => new ReminderChoice(o, o.Title())).ToArray();

    public override string ToString() => Title;
}
