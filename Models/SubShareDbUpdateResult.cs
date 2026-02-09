namespace AnimeFolderOrganizer.Models;

/// <summary>
/// 表示 sub_share 資料庫更新或匯入作業的結果。
/// </summary>
public sealed record SubShareDbUpdateResult
{
    /// <summary>
    /// 作業是否成功。
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 錯誤訊息（若作業失敗）。
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 新資料庫檔案的大小（位元組）。
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// 作業成功時的時間戳記（UTC）。
    /// </summary>
    public DateTime TimestampUtc { get; init; }

    /// <summary>
    /// 成功建立的工廠方法。
    /// </summary>
    public static SubShareDbUpdateResult Succeeded(long size)
    {
        return new SubShareDbUpdateResult
        {
            IsSuccess = true,
            Size = size,
            TimestampUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 失敗時建立的工廠方法。
    /// </summary>
    public static SubShareDbUpdateResult Failed(string errorMessage)
    {
        return new SubShareDbUpdateResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
