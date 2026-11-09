using System.ComponentModel;
using DDL.Models;

namespace DDL.ViewModels;

/// One cell of the month calendar.
public sealed class CalendarDay : INotifyPropertyChanged
{
    private bool _isSelected;

    public CalendarDay(DateTime date, bool isInMonth, bool isToday, bool isSelected,
        IReadOnlyList<DdlTask> tasks, DateTime now)
    {
        Date = date;
        IsInMonth = isInMonth;
        IsToday = isToday;
        _isSelected = isSelected;
        Count = tasks.Count;

        // Up to three dots, most urgent first.
        Dots = tasks.Select(t => TaskRow.UrgencyOf(t, now)).OrderBy(u => u).Take(3).ToList();
        Tooltip = Count == 0
            ? null
            : string.Join("\n", tasks.OrderBy(t => t.DueDate)
                .Select(t => $"{t.DueDate.ToString("HH:mm", Format.English)}  {t.Title}{(t.IsCompleted ? "  (done)" : "")}"));
        AutomationName = date.ToString("dddd, MMMM d", Format.English)
            + (Count switch { 0 => "", 1 => ", 1 deadline", _ => $", {Count} deadlines" });
    }

    public DateTime Date { get; }
    public string DayNumber => Date.Day.ToString();
    public bool IsInMonth { get; }
    public bool IsToday { get; }
    public int Count { get; }
    public IReadOnlyList<Urgency> Dots { get; }
    public string? Tooltip { get; }
    public string AutomationName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
