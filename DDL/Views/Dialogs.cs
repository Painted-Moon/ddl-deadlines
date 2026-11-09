using System.Windows;

namespace DDL.Views;

public static class Dialogs
{
    public static bool ConfirmDelete(Window owner, string title) =>
        MessageBox.Show(owner, $"Delete \"{title}\"? This can't be undone.", "Delete deadline",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
}
