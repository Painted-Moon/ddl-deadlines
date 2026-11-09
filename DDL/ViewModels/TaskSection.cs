using System.ComponentModel;

namespace DDL.ViewModels;

/// A titled group of task cards ("Today", "Completed", ...).
public sealed class TaskSection(string title, IReadOnlyList<TaskRow> rows, bool isCollapsible = false, bool isExpanded = true)
    : INotifyPropertyChanged
{
    private bool _isExpanded = isExpanded;

    public string Title { get; } = title;
    public IReadOnlyList<TaskRow> Rows { get; } = rows;
    public int Count => Rows.Count;
    public bool IsCollapsible { get; } = isCollapsible;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
