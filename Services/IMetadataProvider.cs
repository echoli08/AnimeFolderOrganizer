using System.Collections.Generic;
using System.Threading.Tasks;

namespace AnimeFolderOrganizer.Services;

public record AnimeMetadata(
    string Id,
    string TitleJP,
    string TitleCN,
    string TitleTW,
    string TitleEN,
    string Type,
    int? Year,
    double Confidence
);

/// <summary>
/// 定義外部動畫資訊來源的介面
/// </summary>
public interface IMetadataProvider
{
    string ProviderName { get; }

    /// <summary>
    /// 根據資料夾名稱分析並搜尋動畫資訊
    /// </summary>
    Task<AnimeMetadata?> AnalyzeAsync(string folderName);

    /// <summary>
    /// 批次分析多個資料夾名稱，回傳結果順序需與輸入一致
    /// </summary>
    Task<IReadOnlyList<AnimeMetadata?>> AnalyzeBatchAsync(IReadOnlyList<string> folderNames);
}
