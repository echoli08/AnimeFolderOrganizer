namespace AnimeFolderOrganizer.Services;

/// <summary>
/// 資料夾選取服務介面
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// 開啟資料夾選取對話框
    /// </summary>
    /// <returns>選取的路徑，若取消則回傳 null</returns>
    string? PickFolder();
}
