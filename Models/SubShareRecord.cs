namespace AnimeFolderOrganizer.Models;

/// <summary>
/// 代表 sub_share db.xml 中的一筆 &lt;subs&gt; 資料。
/// 用於從 XML 反序列化後的基礎資料結構。
/// </summary>
public sealed class SubShareRecord
{
    /// <summary>字幕發布時間</summary>
    public DateTimeOffset Time { get; init; }

    /// <summary>簡體中文名稱</summary>
    public string NameChs { get; init; } = string.Empty;

    /// <summary>繁體中文名稱</summary>
    public string NameCht { get; init; } = string.Empty;

    /// <summary>日文名稱</summary>
    public string NameJp { get; init; } = string.Empty;

    /// <summary>英文名稱</summary>
    public string NameEn { get; init; } = string.Empty;

    /// <summary>羅馬拼音名稱</summary>
    public string NameRome { get; init; } = string.Empty;

    /// <summary>作品類型 (TV/OVA/劇場版等)</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>來源 (BD/DVD等)</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>字幕組名稱</summary>
    public string SubName { get; init; } = string.Empty;

    /// <summary>副檔名 (如 srt、ass)</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>字幕提供者</summary>
    public string Providers { get; init; } = string.Empty;

    /// <summary>字幕描述說明</summary>
    public string Desc { get; init; } = string.Empty;

    /// <summary>字幕檔案相對路徑</summary>
    public string Path { get; init; } = string.Empty;
}
