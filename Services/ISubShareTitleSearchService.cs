using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// sub_share 標題索引與關鍵字搜尋服務。
/// 負責載入並快取本機 db.xml，提供 LIKE(包含) 行為的查詢。
/// </summary>
public interface ISubShareTitleSearchService
{
    Task<SubShareSearchDiagnostics> GetDiagnosticsAsync(CancellationToken ct);
    /// <summary>
    /// 確保資料庫已載入到記憶體。
    /// 若 db.xml 已更新，需能重新載入。
    /// </summary>
    Task EnsureLoadedAsync(CancellationToken ct);

    /// <summary>
    /// 以關鍵字搜尋作品標題（包含比對）。
    /// </summary>
    /// <param name="keyword">關鍵字。</param>
    /// <param name="limit">最多回傳筆數。</param>
    /// <param name="ct">取消權杖。</param>
    Task<IReadOnlyList<SubShareTitleMatch>> SearchAsync(string keyword, int limit, CancellationToken ct);

    /// <summary>
    /// 嘗試從 sub_share 資料庫找出最接近的作品。
    /// 先做快速 exact 對應（正規化後），失敗再退回關鍵字搜尋。
    /// </summary>
    Task<SubShareTitleMatch?> FindBestMatchAsync(string title, CancellationToken ct);
}
