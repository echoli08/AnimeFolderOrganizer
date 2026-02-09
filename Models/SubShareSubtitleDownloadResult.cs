namespace AnimeFolderOrganizer.Models;

/// <summary>
/// sub_share 字幕下載結果
/// </summary>
public sealed record SubShareSubtitleDownloadResult(
    int DownloadedCount,
    int SkippedCount,
    string? ErrorMessage);
