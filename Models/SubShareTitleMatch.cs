namespace AnimeFolderOrganizer.Models;

/// <summary>
/// 表示 sub_share 搜尋結果的標題匹配資料，用於 UI 顯示與名稱對應。
/// </summary>
public sealed class SubShareTitleMatch
{
    /// <summary>
    /// 穩定的唯一鍵值，由標準化後日文名稱與類型組成 (如 "刀劍神域_TV")。
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>簡體中文標題</summary>
    public string TitleChs { get; init; } = string.Empty;

    /// <summary>繁體中文標題</summary>
    public string TitleCht { get; init; } = string.Empty;

    /// <summary>日文標題 (通常作為標準化依據)</summary>
    public string TitleJp { get; init; } = string.Empty;

    /// <summary>英文標題</summary>
    public string TitleEn { get; init; } = string.Empty;

    /// <summary>羅馬拼音標題</summary>
    public string TitleRome { get; init; } = string.Empty;

    /// <summary>作品類型 (TV/OVA/劇場版等)，可為 null</summary>
    public string? Type { get; init; }

    /// <summary>最近更新時間，可為 null</summary>
    public DateTimeOffset? Time { get; init; }

    /// <summary>
    /// 指向 GitHub repo 內字幕資料夾的相對路徑（用於後續遞迴列舉/下載字幕）。
    /// 例：subs_list/animation/1988/(1988.4.16)龙猫
    /// </summary>
    public string RepoPath { get; init; } = string.Empty;
}
