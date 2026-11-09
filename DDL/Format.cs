using System.Globalization;

namespace DDL;

/// Date text shown in the app and in reminders.
public static class Format
{
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static string Due(DateTime due) =>
        due.ToString(due.Year == DateTime.Now.Year ? "ddd, MMM d · HH:mm" : "ddd, MMM d, yyyy · HH:mm", English);

    /// "Today · 18:00", "Tomorrow · 09:30", otherwise like Due().
    public static string DueNear(DateTime due, DateTime now)
    {
        var time = due.ToString("HH:mm", English);
        return (due.Date - now.Date).Days switch
        {
            0 => $"Today · {time}",
            1 => $"Tomorrow · {time}",
            -1 => $"Yesterday · {time}",
            _ => Due(due),
        };
    }

    /// "in 3 h", "in 2 days", "45 min overdue", "due now".
    public static string Relative(DateTime due, DateTime now)
    {
        var span = due - now;
        var size = span.Duration();
        if (size.TotalMinutes < 1) return "due now";

        var amount = size.TotalHours < 1 ? $"{(int)size.TotalMinutes} min"
                   : size.TotalHours < 24 ? $"{(int)size.TotalHours} h"
                   : (int)size.TotalDays == 1 ? "1 day"
                   : $"{(int)size.TotalDays} days";
        return span > TimeSpan.Zero ? $"in {amount}" : $"{amount} overdue";
    }
}
