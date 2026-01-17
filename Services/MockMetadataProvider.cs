using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace AnimeFolderOrganizer.Services;

// 更新 Mock 實作以符合新介面
public class MockMetadataProvider : IMetadataProvider
{
    public string ProviderName => "Mock Provider";

    public Task<AnimeMetadata?> AnalyzeAsync(string folderName)
    {
        // 簡單模擬：假設都辨識為某部動畫
        return Task.FromResult<AnimeMetadata?>(new AnimeMetadata(
            Id: "12345",
            TitleJP: "葬送のフリーレン",
            TitleCN: "葬送的芙莉莲",
            TitleTW: "葬送的芙莉蓮",
            TitleEN: "Frieren: Beyond Journey's End",
            Year: 2023,
            Confidence: 0.95
        ));
    }

    public Task<IReadOnlyList<AnimeMetadata?>> AnalyzeBatchAsync(IReadOnlyList<string> folderNames)
    {
        var list = folderNames.Select(_ => (AnimeMetadata?)new AnimeMetadata(
            Id: "12345",
            TitleJP: "葬送のフリーレン",
            TitleCN: "葬送的芙莉莲",
            TitleTW: "葬送的芙莉蓮",
            TitleEN: "Frieren: Beyond Journey's End",
            Year: 2023,
            Confidence: 0.95
        )).ToList();

        return Task.FromResult<IReadOnlyList<AnimeMetadata?>>(list);
    }
}
