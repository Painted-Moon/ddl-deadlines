using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DDL.Models;
using DDL.ViewModels;

namespace DDL.Views;

/// Month grid with a dot per deadline. Click selects a day; double-click asks for a new deadline on it.
public partial class MonthCalendar : UserControl
{
    private IReadOnlyList<DdlTask> _tasks = [];
    private DateTime _month = FirstOfMonth(DateTime.Today);
    private List<CalendarDay> _days = [];

    public MonthCalendar()
    {
        InitializeComponent();
        var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        WeekdayHeader.ItemsSource = Enumerable.Range(0, 7)
            .Select(i => Format.English.DateTimeFormat.ShortestDayNames[((int)first + i) % 7])
            .ToList();
        Rebuild();
    }

    public DateTime? SelectedDate { get; private set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler<DateTime>? DayActivated;

    /// Redraws the dots. Call whenever tasks change (also refreshes "today").
    public void SetTasks(IReadOnlyList<DdlTask> tasks)
    {
        _tasks = tasks;
        Rebuild();
    }

    public void Select(DateTime? date)
    {
        var day = date?.Date;
        if (day is { } d && FirstOfMonth(d) != _month)
        {
            var direction = FirstOfMonth(d) > _month ? 1 : -1;
            _month = FirstOfMonth(d);
            SelectedDate = day;
            Rebuild();
            SlideIn(direction);
        }
        else
        {
            SelectedDate = day;
            foreach (var cell in _days) cell.IsSelected = cell.Date == day;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild()
    {
        MonthTitle.Text = _month.ToString("MMMM yyyy", Format.English);

        var firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var start = _month.AddDays(-(((int)_month.DayOfWeek - (int)firstDay + 7) % 7));
        var now = DateTime.Now;
        var byDay = _tasks.GroupBy(t => t.DueDate.Date).ToDictionary(g => g.Key, g => (IReadOnlyList<DdlTask>)g.ToList());

        _days = Enumerable.Range(0, 42).Select(i =>
        {
            var date = start.AddDays(i);
            return new CalendarDay(date, date.Month == _month.Month, date == now.Date, date == SelectedDate,
                byDay.TryGetValue(date, out var due) ? due : [], now);
        }).ToList();
        Days.ItemsSource = _days;
    }

    private void OnDayClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CalendarDay day) Select(day.Date);
    }

    private void OnDayDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CalendarDay day) DayActivated?.Invoke(this, day.Date);
    }

    private void OnPreviousMonthClick(object sender, RoutedEventArgs e) => ShowMonth(_month.AddMonths(-1));
    private void OnNextMonthClick(object sender, RoutedEventArgs e) => ShowMonth(_month.AddMonths(1));
    private void OnTodayClick(object sender, RoutedEventArgs e) => Select(DateTime.Today);

    private void ShowMonth(DateTime month)
    {
        var direction = month > _month ? 1 : -1;
        _month = month;
        Rebuild();
        SlideIn(direction);
    }

    /// The new month slides in from the side you're moving toward (+1 = later months, from the right).
    private void SlideIn(int direction)
    {
        Motion.Enter(Days, dx: 28 * direction, dy: 0, durationMs: 220);
        Motion.Enter(MonthTitle, dx: 14 * direction, dy: 0, durationMs: 200);
    }

    private static DateTime FirstOfMonth(DateTime d) => new(d.Year, d.Month, 1);
}
