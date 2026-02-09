using System.Windows;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// WPF 彈窗服務實作
/// </summary>
public class WpfDialogService : IDialogService
{
    /// <inheritdoc />
    public void ShowError(string title, string message)
    {
        _ = MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
