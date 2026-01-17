using Microsoft.Win32;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// 實作資料夾選取服務
/// </summary>
public class FolderPickerService : IFolderPickerService
{
    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "請選擇動畫根目錄",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            return dialog.FolderName;
        }

        return null;
    }
}
