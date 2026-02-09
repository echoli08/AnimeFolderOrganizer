namespace AnimeFolderOrganizer.Services;

/// <summary>
/// 彈窗服務介面
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// 顯示錯誤訊息彈窗
    /// </summary>
    /// <param name="title">標題</param>
    /// <param name="message">訊息內容</param>
    void ShowError(string title, string message);
}
