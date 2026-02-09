namespace AnimeFolderOrganizer.Models;

/// <summary>
/// 表示 sub_share 本地資料庫的目前狀態資訊。
/// </summary>
public sealed record SubShareDbStatus
{
    /// <summary>
    /// 資料庫檔案是否存在。
    /// </summary>
    public bool Exists { get; init; }

    /// <summary>
    /// 資料庫檔案大小（單位：位元組）。若檔案不存在則為 0。
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// 資料庫檔案最後寫入時間（UTC）。
    /// </summary>
    public DateTime? LastWriteUtc { get; init; }

    /// <summary>
    /// 資料庫來源的遠端 URL。
    /// </summary>
    public string SourceUrl { get; init; } = string.Empty;
}
