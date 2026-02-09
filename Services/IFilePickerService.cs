namespace AnimeFolderOrganizer.Services;

/// <summary>
/// 檔案選取服務介面
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// 開啟檔案選取對話框
    /// </summary>
    /// <param name="title">對話框標題</param>
    /// <param name="filter">檔案篩選條件 (例如: "XML 檔案|*.xml|所有檔案|*.*")</param>
    /// <param name="defaultExt">預設副檔名 (不含點，例如: "xml")</param>
    /// <returns>選取的檔案路徑，若取消則回傳 null</returns>
    string? PickFile(string title, string filter, string? defaultExt);

    /// <summary>
    /// 開啟檔案多選對話框
    /// </summary>
    /// <returns>選取的檔案路徑清單，若取消則回傳空集合</returns>
    IReadOnlyList<string> PickFiles(string title, string filter, string? defaultExt);
}
