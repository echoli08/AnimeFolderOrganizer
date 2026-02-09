using Microsoft.Win32;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// 實作檔案選取服務
/// </summary>
public class FilePickerService : IFilePickerService
{
    public string? PickFile(string title, string filter, string? defaultExt)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExt != null ? $"*.{defaultExt}" : null
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FileName;
        }

        return null;
    }

    public IReadOnlyList<string> PickFiles(string title, string filter, string? defaultExt)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExt,
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FileNames;
        }

        return Array.Empty<string>();
    }
}
