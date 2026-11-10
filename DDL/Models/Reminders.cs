namespace DDL.Models;

/// Reminders are stored as minutes before the due time; 0 means "at the due time".
/// A deadline can have several (for example 1 day and 1 hour before).
public static class Reminders
{
    /// Offsets offered as ready-made pills in the editor.
    public static IReadOnlyList<int> Presets { get; } = [0, 15, 60, 1440];

    /// What a new deadline starts with: 1 hour before.
    public static IReadOnlyList<int> Default { get; } = [60];

    public const int MaxCount = 10;
    public const int MaxMinutes = 60 * 24 * 60;   // 60 days

    /// "45 min", "1 hour", "3 hours", "2 days" (whole units only; 90 min stays "90 min").
    public static string Amount(int minutes) =>
        minutes % 1440 == 0 ? Plural(minutes / 1440, "day")
        : minutes % 60 == 0 ? Plural(minutes / 60, "hour")
        : $"{minutes} min";

    /// Pill text in the editor.
    public static string ChipLabel(int minutes) => minutes == 0 ? "At the time" : Amount(minutes);

    /// Spoken/accessible name of one reminder.
    public static string Sentence(int minutes) => minutes == 0 ? "At the due time" : $"{Amount(minutes)} before";

    /// Short text on a deadline card: "1 hour before", "1 d, 1 h before", "1 h before · at due time".
    public static string Summary(IReadOnlyCollection<int> offsets)
    {
        var distinct = offsets.Distinct().ToList();
        if (distinct.Count == 0) return "";
        if (distinct.Count == 1) return distinct[0] == 0 ? "At due time" : Sentence(distinct[0]);

        var before = distinct.Where(m => m > 0).OrderByDescending(m => m).Select(Short).ToList();
        var atDue = distinct.Contains(0);
        if (before.Count == 0) return "At due time";
        var text = string.Join(", ", before) + " before";
        return atDue ? text + " · at due time" : text;
    }

    /// Full sentence under the pills: "You'll be reminded 1 day and 1 hour before, and at the due time."
    public static string Describe(IReadOnlyCollection<int> offsets)
    {
        var distinct = offsets.Distinct().ToList();
        if (distinct.Count == 0) return "No reminders for this deadline.";
        var before = distinct.Where(m => m > 0).OrderByDescending(m => m).Select(Amount).ToList();
        var atDue = distinct.Contains(0);
        if (before.Count == 0) return "You'll be reminded at the due time.";
        var list = before.Count switch
        {
            1 => before[0],
            2 => $"{before[0]} and {before[1]}",
            _ => string.Join(", ", before.Take(before.Count - 1)) + ", and " + before[^1],
        };
        return $"You'll be reminded {list} before" + (atDue ? ", and at the due time." : ".");
    }

    private static string Short(int minutes) =>
        minutes % 1440 == 0 ? $"{minutes / 1440} d"
        : minutes % 60 == 0 ? $"{minutes / 60} h"
        : $"{minutes} min";

    private static string Plural(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}

/// The single reminder setting stored by DDL 0.1. Only read, to upgrade old files.
public enum LegacyReminder
{
    Off = -1,
    AtDue = 0,
    Minutes15 = 15,
    OneHour = 60,
    OneDay = 1440,
}
