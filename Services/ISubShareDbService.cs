using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// sub_share 本地資料庫管理服務介面。
/// 負責管理資料庫的取得、更新與匯入作業。
/// </summary>
public interface ISubShareDbService
{
    /// <summary>
    /// 取得目前本地資料庫的狀態資訊。
    /// </summary>
    /// <returns>資料庫狀態，包含存在與否、大小、最後寫入時間與來源 URL。</returns>
    Task<SubShareDbStatus> GetStatusAsync();

    /// <summary>
    /// 從遠端下載並更新本地資料庫檔案。
    /// 會先下載至暫存檔，驗證成功後才進行原子性替換。
    /// </summary>
    /// <param name="ct">取消權杖。</param>
    /// <returns>更新結果。</returns>
    Task<SubShareDbUpdateResult> UpdateFromRemoteAsync(CancellationToken ct);

    /// <summary>
    /// 從本機檔案匯入資料庫。
    /// 會先複製至暫存檔，驗證成功後才進行原子性替換。
    /// </summary>
    /// <param name="filePath">來源檔案路徑。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>匯入結果。</returns>
    Task<SubShareDbUpdateResult> ImportFromFileAsync(string filePath, CancellationToken ct);
}
